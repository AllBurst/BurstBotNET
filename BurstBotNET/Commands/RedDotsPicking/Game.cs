using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.RedDotsPicking;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Models.Localization.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Rest.Results;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.RedDotsPicking;

using RedDotsGame =
    IGame<RedDotsGameState, RawRedDotsGameState, RedDotsPicking, RedDotsPlayerState, RedDotsGameProgress,
        RedDotsInGameRequestType>;

public partial class RedDotsPicking : RedDotsGame
{
    public static async Task<bool> HandleProgress(string messageContent, RedDotsGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawRedDotsGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null) return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (gameState.Progress != deserializedIncomingData.Progress)
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData, gameState, state,
                    channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await SendProgressMessages(gameState, deserializedIncomingData, state,
                channelApi, logger);
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released after sending progress messages");
        }
        catch (JsonSerializationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            return Utilities.HandleException(ex, messageContent, gameState.Semaphore, logger);
        }

        return true;
    }

    public static async Task<bool> HandleProgressChange(RawRedDotsGameState deserializedIncomingData,
        RedDotsGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result1 = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerNewState);
            var result2 = gameState.Players.TryGetValue(playerId, out var previousPlayerOldState);
            if (result1 && result2)
            {
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state,
                    channelApi, logger);
            }
        }

        switch (deserializedIncomingData.Progress)
        {
            case RedDotsGameProgress.Ending:
                return true;
            case RedDotsGameProgress.Progressing:
                await SendPlayingMessage(gameState, deserializedIncomingData, state, channelApi, logger);
                break;
        }

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);
        
        return true;
    }

    public static async Task HandleEndingResult(string messageContent, RedDotsGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        try
        {
            var endingData = JsonSerializer.Deserialize<RedDotsInGameResponseEndingData>(messageContent);
            if (endingData == null) return;

            state.Progress = RedDotsGameProgress.Ending;
            var winnerId = endingData
                .Rewards
                .MaxBy(pair => pair.Value)
                .Key;
            var winner = endingData
                .Players
                .FirstOrDefault(p => p.Value.PlayerId == winnerId)
                .Value;

            var localization = localizations.GetLocalization().RedDotsPicking;

            var title = localization.WinTitle.Replace("{playerName}", winner.PlayerName);
            
            var rewardsDescription = endingData.Rewards
                .Select(pair => localization.WinDescription
                    .Replace("{playerName}", endingData.Players[pair.Key].PlayerName)
                    .Replace("{verb}", pair.Value > 0 ? localization.Won : localization.Lost)
                    .Replace("{totalRewards}", Math.Abs(pair.Value).ToString(CultureInfo.InvariantCulture)));

            var description = string.Join('\n', rewardsDescription);
            
            var embed = new Embed(
                title,
                Description: description,
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
                Image: new EmbedImage(winner.AvatarUrl));

            var fields = new List<EmbedField>(endingData.Players.Count);

            foreach (var (pId, player) in endingData.Players)
            {
                fields.Add(new EmbedField(player.PlayerName,
                    endingData.Rewards[pId].ToString(CultureInfo.InvariantCulture), true));
            }

            embed = embed with { Fields = fields };

            foreach (var (pId, player) in state.Players)
            {
                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel!.ID,
                        embeds: new[] { embed });
                if (!sendResult.IsSuccess)
                    logger.LogError(
                        "Failed to broadcast ending result in player {PlayerId}'s channel: {Reason}, inner: {Inner}",
                        pId, sendResult.Error.Message, sendResult.Inner);
            }

            await state.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                0,
                JsonSerializer.SerializeToUtf8Bytes(new RedDotsInGameRequest
                {
                    GameId = state.GameId,
                    RequestType = RedDotsInGameRequestType.Close,
                    PlayerId = 0
                })));
        }
        catch (Exception ex)
        {
            Utilities.HandleException(ex, messageContent, state.Semaphore, logger);
        }
    }

    private static async Task SendProgressMessages(
        RedDotsGameState gameState,
        RawRedDotsGameState? deserializedIncomingData,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (deserializedIncomingData == null) return;

        var getPlayerStateResult = gameState
            .Players
            .TryGetValue(deserializedIncomingData.PreviousPlayerId, out var previousPlayerOldState);
        if (!getPlayerStateResult)
        {
            logger.LogError("Failed to get player {PlayerId}'s old state", deserializedIncomingData.PreviousPlayerId);
            return;
        }

        getPlayerStateResult = deserializedIncomingData
            .Players.TryGetValue(deserializedIncomingData.PreviousPlayerId, out var previousPlayerNewState);
        if (!getPlayerStateResult)
        {
            logger.LogError("Failed to get player {PlayerId}'s new state", deserializedIncomingData.PreviousPlayerId);
            return;
        }

        logger.LogDebug("Sending progress messages...");
        switch (gameState.Progress)
        {
            case RedDotsGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState!, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case RedDotsGameProgress.Progressing:
            case RedDotsGameProgress.Ending:
            {
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state, channelApi, logger);

                if (deserializedIncomingData.Progress.Equals(RedDotsGameProgress.Ending)) return;

                await SendPlayingMessage(gameState, deserializedIncomingData, state,
                    channelApi, logger);
                
                break;
            }
        }
    }

    private static async Task SendInitialMessage(
        RedDotsPlayerState? playerState,
        RawRedDotsPlayerState newPlayerState,
        RawRedDotsGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().RedDotsPicking;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);

        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{helpText}", localization.CommandList["general"])
            .Replace("{cardNames}", cardNames);

        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);
        await using var renderedTable = SkiaService.RenderDeck(deckService, deserializedIncomingData.CardsOnTable);

        var userEmbed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

        var tableCardNames = string.Join('\n', deserializedIncomingData.CardsOnTable);
        var randomFileName = Utilities.GenerateRandomString() + ".jpg";
        var randomUri = $"attachment://{randomFileName}";

        var tableEmbed = new Embed(
            Description: localization.CardsOnTable.Replace("{cardNames}", tableCardNames),
            Colour: BurstColor.Burst.ToColor(),
            Image: new EmbedImage(randomUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Footer: new EmbedFooter(localization.InitialMessageFooter));

        var attachments = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedDeck)),
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(randomFileName, renderedTable)),
        };

        var sendResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { userEmbed, tableEmbed },
                attachments: attachments);
        if (!sendResult.IsSuccess)
            logger.LogError("Failed to send initial message to player {PlayerId}: {Reason}, inner: {Inner}",
                newPlayerState.PlayerId,
                sendResult.Error.Message, sendResult.Inner);
    }

    private static async Task SendPlayingMessage(
        RedDotsGameState gameState,
        RawRedDotsGameState deserializedIncomingData,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = state.Localizations.GetLocalization();
        var redDotsLocalization = localization.RedDotsPicking;

        var nextPlayer = deserializedIncomingData
            .Players
            .FirstOrDefault(p => p.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        await using var cardsOnTable = deserializedIncomingData.CardsOnTable.Count > 0
            ? SkiaService.RenderDeck(state.DeckService, deserializedIncomingData.CardsOnTable)
            : null;

        foreach (var (playerId, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null) continue;

            var embeds = BuildPlayingMessage(playerState, nextPlayer, deserializedIncomingData, state.Localizations);

            if (nextPlayer.PlayerId == playerId)
            {
                var attachments = new List<OneOf<FileData, IPartialAttachment>>();
                
                await using var nextPlayerDeck = SkiaService
                    .RenderDeck(state.DeckService, nextPlayer.Cards);
                attachments.Add(OneOf<FileData, IPartialAttachment>.FromT0(new FileData("playerDeck.jpg", nextPlayerDeck)));
                
                var components = BuildPlayingComponents(nextPlayer, deserializedIncomingData, redDotsLocalization,
                    out _);

                if (cardsOnTable != null)
                {
                    await using var imageCopy = new MemoryStream((int)cardsOnTable.Length);
                    await cardsOnTable.CopyToAsync(imageCopy);
                    cardsOnTable.Seek(0, SeekOrigin.Begin);
                    imageCopy.Seek(0, SeekOrigin.Begin);
                    attachments.Add(OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, imageCopy)));
                    
                    var result = await channelApi
                        .CreateMessageAsync(playerState.TextChannel.ID,
                            embeds: embeds,
                            components: components,
                            attachments: attachments);

                    if (result.IsSuccess) continue;

                    var error = result.Error as RestResultError<RestError>;

                    logger.LogError("Failed to send playing message to the player {PlayerId}: {Reason}, inner: {Inner}",
                        playerId, result.Error.Message, error?.Error);
                }
                
                var sendResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: embeds,
                        components: components,
                        attachments: attachments);

                if (sendResult.IsSuccess) continue;

                var restError = sendResult.Error as RestResultError<RestError>;

                logger.LogError("Failed to send playing message to the player {PlayerId}: {Reason}, inner: {Inner}",
                    playerId, sendResult.Error.Message, restError?.Error);
            }
            else
            {
                var attachments = new List<OneOf<FileData, IPartialAttachment>>();

                if (cardsOnTable != null)
                {
                    await using var imageCopy = new MemoryStream((int)cardsOnTable.Length);
                    await cardsOnTable.CopyToAsync(imageCopy);
                    cardsOnTable.Seek(0, SeekOrigin.Begin);
                    imageCopy.Seek(0, SeekOrigin.Begin);
                    attachments.Add(OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, imageCopy)));
                    
                    var result = await channelApi
                        .CreateMessageAsync(playerState.TextChannel.ID,
                            embeds: embeds,
                            attachments: attachments);
                
                    if (result.IsSuccess) continue;
                
                    var error = result.Error as RestResultError<RestError>;
                
                    logger.LogError("Failed to send playing message to the player {PlayerId}: {Reason}, inner: {Inner}",
                        playerId, result.Error.Message, error?.Error);
                }

                var sendResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: embeds,
                        attachments: attachments);
                
                if (sendResult.IsSuccess) continue;
                
                var restError = sendResult.Error as RestResultError<RestError>;
                
                logger.LogError("Failed to send playing message to the player {PlayerId}: {Reason}, inner: {Inner}",
                    playerId, sendResult.Error.Message, restError?.Error);
            }
        }
    }

    private static IMessageComponent[] BuildPlayingComponents(
        RawRedDotsPlayerState nextPlayer,
        RawRedDotsGameState deserializedIncomingData,
        RedDotsLocalization localization,
        out RedDotsSelectMenuType selectMenuType)
    {
        var helpButton = new ButtonComponent(ButtonComponentStyle.Primary, localization.ShowHelp,
            new PartialEmoji(Name: "❓"), "red_dots_help");
        
        // Force player to eliminate red fives
        var hasRedFives = deserializedIncomingData
            .CardsOnTable
            .Any(c => c.Suit is Suit.Heart or Suit.Diamond && c.Number == 5);
        var playerHasFive = nextPlayer
            .Cards
            .Any(c => c.Suit is Suit.Spade or Suit.Club && c.Number == 5);
        var hasFourPlayers = deserializedIncomingData.Players.Count == 4;
        if (hasRedFives && playerHasFive && hasFourPlayers)
        {
            var selectOptions = nextPlayer
                .Cards
                .Where(c => c.Number == 5)
                .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(), new PartialEmoji(c.Suit.ToSnowflake())));
            var selectMenu = new SelectMenuComponent("red_dots_force_five_selection",
                selectOptions.ToImmutableArray(),
                localization.Use, 1, 1);

            selectMenuType = RedDotsSelectMenuType.ForcePlayFive;

            return new IMessageComponent[]
            {
                new ActionRowComponent(new[]
                {
                    selectMenu
                }),
                new ActionRowComponent(new[]
                {
                    helpButton
                })
            };
        }

        var availableCards = new List<Card>();
        var availableTableCards = new List<Card>();
        foreach (var card in nextPlayer.Cards)
        {
            foreach (var tableCard in deserializedIncomingData.CardsOnTable.Where(tableCard => Card.CanCombine(card, tableCard)))
            {
                availableCards.Add(card);
                availableTableCards.Add(tableCard);
            }
        }

        if (availableCards.Count > 0)
        {
            var userSelectOptions = availableCards
                .Distinct()
                .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                    new PartialEmoji(c.Suit.ToSnowflake())));
            var userSelectMenu = new SelectMenuComponent("red_dots_user_selection",
                userSelectOptions.ToImmutableArray(),
                localization.Use, 1, 1);
            
            var tableSelectOptions = availableTableCards
                .Distinct()
                .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                    new PartialEmoji(c.Suit.ToSnowflake())));
            var tableSelectMenu = new SelectMenuComponent("red_dots_table_selection",
                tableSelectOptions.ToImmutableArray(),
                localization.Use, 1, 1);
            
            selectMenuType = RedDotsSelectMenuType.Collect;

            return new IMessageComponent[]
            {
                new ActionRowComponent(new[]
                {
                    userSelectMenu
                }),
                new ActionRowComponent(new[]
                {
                    tableSelectMenu
                }),
                new ActionRowComponent(new[]
                {
                    helpButton
                })
            };
        }

        var remainingOptions = nextPlayer
            .Cards
            .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                new PartialEmoji(c.Suit.ToSnowflake())));
        var menu = new SelectMenuComponent("red_dots_give_up_selection",
            remainingOptions.ToImmutableArray(),
            localization.Use, 1, 1);

        selectMenuType = RedDotsSelectMenuType.GiveUp;
        
        return new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                menu
            }),
            new ActionRowComponent(new[]
            {
                helpButton
            })
        };
    }
    
    private static Embed[] BuildPlayingMessage(
        RedDotsPlayerState playerState,
        RawRedDotsPlayerState nextPlayer,
        RawRedDotsGameState deserializedIncomingData,
        Localizations localizations)
    {
        var isNextPlayer = nextPlayer.PlayerId == playerState.PlayerId;
        var localization = localizations.GetLocalization();

        var possessive = isNextPlayer
            ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
            : localization.GenericWords.PossessiveThird.Replace("{playerName}", nextPlayer.PlayerName);

        var title = localization.RedDotsPicking.TurnMessageTitle
            .Replace("{possessive}", possessive);

        var playerEmbed = new Embed(
            Author: new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

        if (isNextPlayer)
        {
            playerEmbed = playerEmbed with
            {
                Description = $"{localization.RedDotsPicking.Cards}\n\n{string.Join('\n', nextPlayer.Cards)}",
                Image = new EmbedImage("attachment://playerDeck.jpg")
            };
        }

        var tableCards = deserializedIncomingData.CardsOnTable;

        var tableEmbed = new Embed(
            Description: localization.RedDotsPicking.CardsOnTable.Replace("{cardNames}", string.Join('\n', tableCards)),
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo)
        );

        if (tableCards.Count > 0)
        {
            tableEmbed = tableEmbed with
            {
                Image = new EmbedImage(Constants.AttachmentUri)
            };
        }

        return new[] { playerEmbed, tableEmbed };
    }

    private static async Task ShowPreviousPlayerAction(
        RedDotsPlayerState previousPlayerOldState,
        RawRedDotsPlayerState previousPlayerNewState,
        RedDotsGameState oldGameState,
        RawRedDotsGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (oldGameState.Progress.Equals(RedDotsGameProgress.Starting)) return;
        
        var localization = state.Localizations.GetLocalization().RedDotsPicking;

        var collectedCardsDiff =
            previousPlayerNewState.CollectedCards.Count - previousPlayerOldState.CollectedCards.Length;

        // Player collected new cards
        if (collectedCardsDiff == 2)
        {
            var usedCard = previousPlayerOldState.Cards
                .Intersect(previousPlayerNewState.CollectedCards)
                .FirstOrDefault();

            var collectedCard = oldGameState.CardsOnTable
                .Intersect(previousPlayerNewState.CollectedCards)
                .FirstOrDefault();
            
            var title = localization.UseMessage
                .Replace("{playerName}", previousPlayerNewState.PlayerName)
                .Replace("{card}", usedCard!.ToString())
                .Replace("{card2}", collectedCard!.ToString());
            
            await using var renderedCards =
                SkiaService.RenderDeck(state.DeckService, new[] { usedCard, collectedCard });

            var lastDrawnCardResultAfterCollection = GetLastDrawnCard(previousPlayerOldState,
                previousPlayerNewState,
                oldGameState,
                newGameState,
                usedCard,
                collectedCard,
                state);

            if (!lastDrawnCardResultAfterCollection.HasValue)
            {
                foreach (var (_, player) in oldGameState.Players)
                {
                    await SendPreviousPlayerActionMessage(player, previousPlayerNewState, renderedCards,
                        null, title, null, localization,
                        channelApi, logger);
                }
            }
            else
            {
                var (lastDrawnCardAfterCollection, lastDrawnCardImageAfterCollection) = lastDrawnCardResultAfterCollection.Value;

                foreach (var (_, player) in oldGameState.Players)
                {
                    await SendPreviousPlayerActionMessage(player, previousPlayerNewState, renderedCards,
                        lastDrawnCardImageAfterCollection, title, lastDrawnCardAfterCollection, localization,
                        channelApi, logger);
                }
            
                await lastDrawnCardImageAfterCollection.DisposeAsync();
            }
            
            return;
        }

        // Player doesn't collect new cards
        var givenUpCard = newGameState.CardsOnTable
            .Intersect(previousPlayerOldState.Cards)
            .FirstOrDefault();
        
        var (lastDrawnCardAfterGivingUp, lastDrawnCardImageAfterGivingUp) = GetLastDrawnCard(previousPlayerOldState,
            previousPlayerNewState,
            oldGameState,
            newGameState,
            givenUpCard!,
            null,
            state)!.Value;

        await using var renderedGivenUpCard = SkiaService.RenderCard(state.DeckService, givenUpCard!);

        var resultTitle = localization.GiveUpMessage
            .Replace("{playerName}", previousPlayerNewState.PlayerName)
            .Replace("{card}", givenUpCard!.ToString());

        foreach (var (_, player) in oldGameState.Players)
        {
            await SendPreviousPlayerActionMessage(player, previousPlayerNewState, renderedGivenUpCard,
                lastDrawnCardImageAfterGivingUp, resultTitle, lastDrawnCardAfterGivingUp, localization,
                channelApi, logger);
        }
        
        await lastDrawnCardImageAfterGivingUp.DisposeAsync();
    }

    private static async Task SendPreviousPlayerActionMessage(
        IPlayerState player,
        RawRedDotsPlayerState previousPlayerNewState,
        Stream renderedCard,
        Stream? lastDrawnCardImage,
        string title,
        Card? lastDrawnCard,
        RedDotsLocalization localization,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (player.TextChannel == null) return;

        await using var imageCopy = new MemoryStream((int)renderedCard.Length);
        await renderedCard.CopyToAsync(imageCopy);
        renderedCard.Seek(0, SeekOrigin.Begin);
        imageCopy.Seek(0, SeekOrigin.Begin);

        var embeds = new List<IEmbed>();
        var attachments = new List<OneOf<FileData, IPartialAttachment>>();

        var randomFileName = Utilities.GenerateRandomString() + ".jpg";
        var randomUri = $"attachment://{randomFileName}";

        var resultEmbed = new Embed(
            Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Image: new EmbedImage(randomUri));
        
        embeds.Add(resultEmbed);
        attachments.Add(OneOf<FileData, IPartialAttachment>.FromT0(new FileData(randomFileName, imageCopy)));

        if (lastDrawnCard != null && lastDrawnCardImage != null)
        {
            var randomFileName2 = Utilities.GenerateRandomString() + ".jpg";
            var randomUri2 = $"attachment://{randomFileName2}";
            
            var drawEmbed = new Embed(
                Author: new EmbedAuthor(previousPlayerNewState.PlayerName,
                    IconUrl: previousPlayerNewState.AvatarUrl),
                Title: localization.DrawMessage
                    .Replace("{playerName}", previousPlayerNewState.PlayerName)
                    .Replace("{card}", lastDrawnCard.ToString()),
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
                Image: new EmbedImage(randomUri2));
            embeds.Add(drawEmbed);
            
            await using var drawCardImageCopy = new MemoryStream((int)lastDrawnCardImage.Length);
            await lastDrawnCardImage.CopyToAsync(drawCardImageCopy);
            lastDrawnCardImage.Seek(0, SeekOrigin.Begin);
            drawCardImageCopy.Seek(0, SeekOrigin.Begin);
            
            attachments.Add(OneOf<FileData, IPartialAttachment>.FromT0(new FileData(randomFileName2, drawCardImageCopy)));
            
            var result = await channelApi
                .CreateMessageAsync(player.TextChannel.ID,
                    embeds: embeds,
                    attachments: attachments);
            
            if (result.IsSuccess) return;
            
            logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                result.Error.Message, result.Inner);

            return;
        }

        var sendResult = await channelApi
            .CreateMessageAsync(player.TextChannel.ID,
                embeds: embeds,
                attachments: attachments);

        if (sendResult.IsSuccess) return;

        logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
            sendResult.Error.Message, sendResult.Inner);
    }

    private static (Card, Stream)? GetLastDrawnCard(
        RedDotsPlayerState previousPlayerOldState,
        RawRedDotsPlayerState previousPlayerNewState,
        RedDotsGameState oldGameState,
        RawRedDotsGameState newGameState,
        Card usedCard,
        Card? collectedCard,
        State state)
    {
        if (previousPlayerOldState.SecondMove) return null;
        
        // Player drew a card that matches a card on the table.
        if (oldGameState.CurrentPlayerOrder == newGameState.CurrentPlayerOrder)
        {
            var drawDiff = previousPlayerNewState
                .Cards
                .Where(c => c.Suit != usedCard.Suit && c.Number != usedCard.Number)
                .Except(previousPlayerOldState
                    .Cards
                    .Where(c => c.Suit != usedCard.Suit && c.Number != usedCard.Number), new CardEqualityComparer());
            var drawnCard = drawDiff.LastOrDefault();

            return (drawnCard!, SkiaService.RenderCard(state.DeckService, drawnCard!));
        }

        // Player drew a card and that card is on the table.
        var tableDiff = newGameState
            .CardsOnTable
            .Where(c => collectedCard != null ||
                        c.Suit != collectedCard!.Suit && c.Number != collectedCard.Number)
            .Except(oldGameState
                    .CardsOnTable
                    .Where(c => collectedCard != null ||
                                c.Suit != collectedCard!.Suit && c.Number != collectedCard.Number),
                new CardEqualityComparer());
        var card = tableDiff.LastOrDefault();

        return (card!, SkiaService.RenderCard(state.DeckService, card!));
    }
    
    private static async Task UpdateGameState(RedDotsGameState state, RawRedDotsGameState? data,
        IDiscordRestGuildAPI guildApi)
    {
        if (data == null) return;

        state.BaseBet = data.BaseBet;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;
        state.CardsOnTable = data.CardsOnTable.ToImmutableArray();

        foreach (var (playerId, playerState) in data.Players)
        {
            if (state.Players.ContainsKey(playerId))
            {
                var oldPlayerState = state.Players.GetOrAdd(playerId, new RedDotsPlayerState());

                oldPlayerState.Cards = playerState.Cards.ToImmutableArray();
                oldPlayerState.Order = playerState.Order;
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayerName = playerState.PlayerName;
                oldPlayerState.Points = playerState.Points;
                oldPlayerState.Score = playerState.Score;
                oldPlayerState.CollectedCards = playerState.CollectedCards.ToImmutableArray();
                oldPlayerState.ScoreAdjustment = playerState.ScoreAdjustment;
                oldPlayerState.SecondMove = playerState.SecondMove;

                if (oldPlayerState.TextChannel != null || playerState.ChannelId == 0) continue;

                oldPlayerState.TextChannel =
                    await Utilities.TryGetTextChannel(state.Guilds, playerState.ChannelId, guildApi);
            }
            else
            {
                var newPlayerState = new RedDotsPlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    Order = playerState.Order,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null,
                    ScoreAdjustment = playerState.ScoreAdjustment,
                    CollectedCards = playerState.CollectedCards.ToImmutableArray(),
                    Score = playerState.Score,
                    SecondMove = playerState.SecondMove,
                    Points = playerState.Points
                };

                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}