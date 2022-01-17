using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.ChaseThePig;

using ChasePigGame = IGame<ChasePigGameState, RawChasePigGameState, ChaseThePig, ChasePigPlayerState, ChasePigGameProgress, ChasePigInGameRequestType>;

public partial class ChaseThePig : ChasePigGame
{
    private static readonly Dictionary<ChasePigExposure, Card> ExposableCards = new()
    {
        { ChasePigExposure.Transformer, Card.Create(Suit.Club, 10) },
        { ChasePigExposure.FirstTransformer, Card.Create(Suit.Club, 10) },
        { ChasePigExposure.DoubleGoat, Card.Create(Suit.Diamond, 11) },
        { ChasePigExposure.FirstDoubleGoat, Card.Create(Suit.Diamond, 11) },
        { ChasePigExposure.DoublePig, Card.Create(Suit.Spade, 12) },
        { ChasePigExposure.FirstDoublePig, Card.Create(Suit.Spade, 12) },
        { ChasePigExposure.DoubleMinus, Card.Create(Suit.Heart, 1) },
        { ChasePigExposure.FirstDoubleMinus, Card.Create(Suit.Heart, 1) }
    };
    
    public static async Task<bool> HandleProgress(string messageContent, ChasePigGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawChasePigGameState>(messageContent, Game.JsonSerializerSettings);
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

            await SendProgressMessages(gameState, deserializedIncomingData, state, channelApi, logger);
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released after sending progress messages");
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception ex)
        {
            return Utilities.HandleException(ex, messageContent, gameState.Semaphore, logger);
        }

        return true;
    }

    public static async Task<bool> HandleProgressChange(RawChasePigGameState deserializedIncomingData, ChasePigGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result1 = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerNewState);
            var result2 = gameState.Players.TryGetValue(playerId, out var previousPlayerOldState);
            if (result1 && result2)
            {
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state, channelApi, logger);
            }
        }

        switch (deserializedIncomingData.Progress)
        {
            case ChasePigGameProgress.Ending:
                return true;
            case ChasePigGameProgress.Exposing:
                await SendExposingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                break;
            case ChasePigGameProgress.Progressing:
                await SendPlayingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                break;
        }

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);

        return true;
    }

    public static async Task HandleEndingResult(string messageContent, ChasePigGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        try
        {
            var endingData = JsonSerializer.Deserialize<ChasePigInGameResponseEndingData>(messageContent);
            if (endingData == null) return;
            
            state.Progress = ChasePigGameProgress.Ending;
            var winnerId = endingData
                .Rewards
                .MaxBy(pair => pair.Value)
                .Key;
            var winner = endingData
                .Players
                .FirstOrDefault(p => p.Value.PlayerId == winnerId)
                .Value;

            var localization = localizations.GetLocalization().ChaseThePig;
            
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
                    endingData.Rewards[pId].ToString(CultureInfo.InvariantCulture) + " tips", true));
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
                JsonSerializer.SerializeToUtf8Bytes(new ChasePigInGameRequest
                {
                    GameId = state.GameId,
                    RequestType = ChasePigInGameRequestType.Close,
                    PlayerId = 0
                })));
        }
        catch (Exception ex)
        {
            Utilities.HandleException(ex, messageContent, state.Semaphore, logger);
        }
    }

    private static async Task SendProgressMessages(ChasePigGameState gameState,
        RawChasePigGameState? deserializedIncomingData, State state, IDiscordRestChannelAPI channelApi, ILogger logger)
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
            case ChasePigGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case ChasePigGameProgress.Exposing:
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state, channelApi, logger);
                await Task.Delay(TimeSpan.FromSeconds(1));
                await SendExposingMessage(gameState,
                    deserializedIncomingData, state.DeckService, state.Localizations,
                    channelApi, logger);
                break;
            case ChasePigGameProgress.Progressing:
            case ChasePigGameProgress.Ending:
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state, channelApi, logger);

                if (deserializedIncomingData.Progress == ChasePigGameProgress.Ending) return;

                await SendPlayingMessage(gameState, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                
                break;
        }
    }

    private static async Task SendInitialMessage(
        IPlayerState? playerState,
        RawChasePigPlayerState? newPlayerState,
        RawChasePigGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null || newPlayerState == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().ChaseThePig;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);
        
        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{helpText}", localization.CommandList["general"])
            .Replace("{cardNames}", cardNames);
        
        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);
        
        var userEmbed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));
        
        var attachments = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedDeck))
        };

        var sendResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { userEmbed },
                attachments: attachments);
        if (!sendResult.IsSuccess)
            logger.LogError("Failed to send initial message to player {PlayerId}: {Reason}, inner: {Inner}",
                newPlayerState.PlayerId,
                sendResult.Error.Message, sendResult.Inner);
    }

    private static async Task SendExposingMessage(
        ChasePigGameState oldGameState,
        RawChasePigGameState? deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (deserializedIncomingData == null) return;

        var localization = localizations.GetLocalization();

        var nextPlayer = deserializedIncomingData
            .Players
            .FirstOrDefault(p => p.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        var exposableCard = nextPlayer
            .Cards
            .Where(c => ExposableCards.ContainsValue(c))
            .ToImmutableArray();

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;
                
            var isNextPlayer = player.PlayerId == nextPlayer.PlayerId;
            
            var title = localization
                .ChaseThePig
                .TurnMessageTitle
                .Replace("{possessive}",
                    isNextPlayer
                        ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
                        : nextPlayer.PlayerName);

            var embed = new Embed
                {
                    Author = new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
                    Colour = BurstColor.Burst.ToColor(),
                    Title = title,
                    Thumbnail = new EmbedThumbnail(Constants.BurstLogo)
                };

                if (isNextPlayer)
                {
                    if (exposableCard.IsEmpty)
                    {
                        embed = embed with
                        {
                            Description = localization.ChaseThePig.NoExposableCardsSecond
                        };

                        var components = new IMessageComponent[]
                        {
                            new ActionRowComponent(new[]
                            {
                                new ButtonComponent(ButtonComponentStyle.Primary, localization.GenericWords.Confirm,
                                    new PartialEmoji(Name: Constants.CheckMark), "chase_pig_confirm_no_exposable_cards"),
                                new ButtonComponent(ButtonComponentStyle.Primary,
                                    localization.ChaseThePig.ShowHelp,
                                    new PartialEmoji(Name: Constants.QuestionMark),
                                    "chase_pig_help")
                            })
                        };
                        
                        var sendResult = await channelApi
                            .CreateMessageAsync(player.TextChannel.ID,
                                embeds: new[] { embed },
                                components: components);
                        if (sendResult.IsSuccess) continue;

                        logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                            player.PlayerId, sendResult.Error.Message, sendResult.Inner);

                        continue;
                    }
                    else
                    {
                        await using var renderedCards = SkiaService.RenderDeck(deckService, exposableCard);

                        embed = embed with
                        {
                            Description = localization.ChaseThePig.Expose,
                            Image = new EmbedImage(Constants.AttachmentUri)
                        };

                        var attachments = new OneOf<FileData, IPartialAttachment>[]
                        {
                            new FileData(Constants.OutputFileName, renderedCards)
                        };

                        var firstCard = exposableCard
                            .FirstOrDefault(c =>
                                c.Suit == nextPlayer.Cards[0].Suit && c.Number == nextPlayer.Cards[0].Number);

                        var selectOptions = exposableCard
                            .Select(c =>
                            {
                                if (firstCard != null && c.Suit == firstCard.Suit && c.Number == firstCard.Number)
                                {
                                    var value = c.ToSpecifier() switch
                                    {
                                        "c10" => ChasePigExposure.FirstTransformer.ToString(),
                                        "sq" => ChasePigExposure.FirstDoublePig.ToString(),
                                        "dj" => ChasePigExposure.FirstDoubleGoat.ToString(),
                                        "ha" => ChasePigExposure.FirstDoubleMinus.ToString(),
                                        _ => string.Empty
                                    };

                                    return new SelectOption(c.ToStringSimple(), value, c.ToStringSimple(),
                                        new PartialEmoji(c.Suit.ToSnowflake()));
                                }

                                var v = c.ToSpecifier() switch
                                {
                                    "c10" => ChasePigExposure.Transformer.ToString(),
                                    "sq" => ChasePigExposure.DoublePig.ToString(),
                                    "dj" => ChasePigExposure.DoubleGoat.ToString(),
                                    "ha" => ChasePigExposure.DoubleMinus.ToString(),
                                    _ => string.Empty
                                };

                                return new SelectOption(c.ToStringSimple(), v, c.ToStringSimple(),
                                    new PartialEmoji(c.Suit.ToSnowflake()));
                            }).ToImmutableArray();

                        var components = new IMessageComponent[]
                        {
                            new ActionRowComponent(new[]
                            {
                                new SelectMenuComponent("chase_pig_expose_menu", selectOptions,
                                    localization.ChaseThePig.Expose, 0, selectOptions.Length)
                            }),
                            new ActionRowComponent(new[]
                            {
                                new ButtonComponent(ButtonComponentStyle.Danger,
                                    localization.ChaseThePig.NoExpose,
                                    new PartialEmoji(Name: Constants.CrossMark),
                                    "chase_pig_decline_expose"),
                                new ButtonComponent(ButtonComponentStyle.Primary,
                                    localization.ChaseThePig.ShowHelp,
                                    new PartialEmoji(Name: Constants.QuestionMark),
                                    "chase_pig_help")
                            })
                        };
                        
                        var sendResult = await channelApi
                            .CreateMessageAsync(player.TextChannel.ID,
                                embeds: new[] { embed },
                                attachments: attachments,
                                components: components);
                        if (sendResult.IsSuccess) continue;

                        logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                            player.PlayerId, sendResult.Error.Message, sendResult.Inner);

                        continue;
                    }
                }

                var result = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed });
                if (result.IsSuccess) continue;

                logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, result.Error.Message, result.Inner);
        }
    }

    private static async Task SendPlayingMessage(
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = localizations.GetLocalization();
        
        var nextPlayer = newGameState
            .Players
            .First(p => p.Value.Order == newGameState.CurrentPlayerOrder)
            .Value;

        var playerCards = nextPlayer
            .Cards
            .Where(c => FilterExposureCards(c, newGameState.Exposures, newGameState))
            .ToImmutableArray()
            .Sort((a, b) => a.Suit.CompareTo(b.Suit) != 0 ? a.Suit.CompareTo(b.Suit) : a.Number.CompareTo(b.Number));

        await using var renderedDeck = SkiaService.RenderDeck(deckService, playerCards);

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;

            var isNextPlayer = player.PlayerId == nextPlayer.PlayerId;

            var title = localization.ChaseThePig.TurnMessageTitle
                .Replace("{possessive}",
                    isNextPlayer
                        ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
                        : nextPlayer.PlayerName);

            var embed = new Embed
            {
                Author = new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
                Title = title,
                Colour = BurstColor.Burst.ToColor(),
                Thumbnail = new EmbedThumbnail(Constants.BurstLogo)
            };

            if (isNextPlayer)
            {
                var description = localization.ChaseThePig.Cards + '\n' + string.Join('\n', playerCards);
                embed = embed with
                {
                    Image = new EmbedImage(Constants.AttachmentUri),
                    Description = description
                };

                var attachment = new OneOf<FileData, IPartialAttachment>[]
                {
                    new FileData(Constants.OutputFileName, renderedDeck)
                };

                if (oldGameState.CardsOnTable.Count > 0)
                {
                    var firstCard = oldGameState.CardsOnTable.First().Item2;
                    var applicableCards = playerCards
                        .Where(c => c.Suit == firstCard.Suit)
                        .ToImmutableArray();
                    playerCards = applicableCards.IsEmpty ? playerCards : applicableCards;
                }

                var selectOptions = playerCards
                    .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                        new PartialEmoji(c.Suit.ToSnowflake())));

                var components = new IMessageComponent[]
                {
                    new ActionRowComponent(new[]
                    {
                        new SelectMenuComponent("chase_pig_card_selection", selectOptions.ToImmutableArray(),
                            localization.ChaseThePig.Play, 1, 1)
                    }),
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary,
                            localization.ChaseThePig.ShowHelp,
                            new PartialEmoji(Name: Constants.QuestionMark),
                            "chase_pig_help")
                    })
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment,
                        components: components);
                if (sendResult.IsSuccess) continue;
                
                logger.LogError("Failed to send playing message to player {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, sendResult.Error.Message, sendResult.Inner);
                
                continue;
            }
            
            var result = await channelApi
                .CreateMessageAsync(player.TextChannel.ID,
                    embeds: new[] { embed });
            if (result.IsSuccess) continue;
                
            logger.LogError("Failed to send playing message to player {PlayerId}: {Reason}, inner: {Inner}",
                player.PlayerId, result.Error.Message, result.Inner);
        }
    }

    private static async Task SendPreviousPlayerExposeMessage(
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = localizations.GetLocalization().ChaseThePig;

        var newExposures = newGameState
            .Exposures
            .Except(oldGameState.Exposures)
            .ToImmutableArray();

        var hasNewExposure = !newExposures.IsEmpty;

        if (!hasNewExposure)
        {
            var embed = new Embed(
                Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

            foreach (var (_, player) in oldGameState.Players)
            {
                if (player.TextChannel == null) continue;

                embed = embed with
                {
                    Title = localization.NoExposeMessage.Replace("{playerName}",
                        player.PlayerId == previousPlayerNewState.PlayerId
                            ? localizations.GetLocalization().GenericWords.Pronoun
                            : previousPlayerNewState.PlayerName)
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed });
                
                if (sendResult.IsSuccess) continue;

                logger.LogError("Failed to send previous player's expose message to {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, sendResult.Error.Message, sendResult.Inner);
            }

            return;
        }

        var exposedCards = newExposures
            .Select(e => ExposableCards[e])
            .ToImmutableArray();

        var titles = newExposures
            .Zip(exposedCards)
            .Select(pair =>
            {
                var (exposure, card) = pair;
                return (exposure switch
                {
                    ChasePigExposure.Transformer or ChasePigExposure.DoubleGoat or ChasePigExposure.DoubleMinus
                        or ChasePigExposure.DoublePig => localization.ExposeMessage,
                    ChasePigExposure.FirstTransformer or ChasePigExposure.FirstDoubleGoat
                        or ChasePigExposure.FirstDoubleMinus
                        or ChasePigExposure.FirstDoublePig => localization.ExposeMessageFirst,
                    _ => string.Empty
                }).Replace("{card}", card.ToStringSimple());
            })
            .ToImmutableArray();

        var descriptions = newExposures
            .Select(e => e switch
            {
                ChasePigExposure.Transformer => localization.ExposeTransformer.Replace("{ratio}", 2.ToString()),
                ChasePigExposure.DoubleGoat => localization.ExposeGoat.Replace("{ratio}", 2.ToString()),
                ChasePigExposure.DoubleMinus => localization.ExposeHeartA.Replace("{ratio}", 2.ToString()),
                ChasePigExposure.DoublePig => localization.ExposePig.Replace("{ratio}", 2.ToString()),
                ChasePigExposure.FirstTransformer => localization.ExposeTransformer.Replace("{ratio}", 4.ToString()),
                ChasePigExposure.FirstDoubleGoat => localization.ExposeGoat.Replace("{ratio}", 4.ToString()),
                ChasePigExposure.FirstDoubleMinus => localization.ExposeHeartA.Replace("{ratio}", 4.ToString()),
                ChasePigExposure.FirstDoublePig => localization.ExposePig.Replace("{ratio}", 4.ToString()),
                _ => ""
            })
            .ToImmutableArray();
        
        await using var renderedCard = SkiaService.RenderDeck(deckService, exposedCards);

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;

            var randomFileName = Utilities.GenerateRandomString() + ".jpg";
            var randomAttachmentUri = $"attachment://{randomFileName}";

            var title = localization.ExposeTitle
                .Replace("{playerName}", player.PlayerId == previousPlayerNewState.PlayerId
                    ? localizations.GetLocalization().GenericWords.Pronoun
                    : previousPlayerNewState.PlayerName);

            var formattedTitles = titles
                .Select(s => s.Replace("{playerName}", player.PlayerId == previousPlayerNewState.PlayerId
                    ? localizations.GetLocalization().GenericWords.Pronoun
                    : previousPlayerNewState.PlayerName));

            var description = string.Join('\n', formattedTitles) + "\n\n" + string.Join('\n', descriptions);

            var embed = new Embed(
                Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                Title: title,
                Description: description,
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
                Image: new EmbedImage(randomAttachmentUri));

            await using var imageCopy = new MemoryStream((int)renderedCard.Length);
            await renderedCard.CopyToAsync(imageCopy);
            renderedCard.Seek(0, SeekOrigin.Begin);
            imageCopy.Seek(0, SeekOrigin.Begin);

            var attachments = new OneOf<FileData, IPartialAttachment>[]
            {
                new FileData(randomFileName, imageCopy)
            };

            var sendResult = await channelApi
                .CreateMessageAsync(player.TextChannel.ID,
                    embeds: new[] { embed },
                    attachments: attachments);
            if (sendResult.IsSuccess) continue;
            
            logger.LogError("Failed to send previous player's expose message to {PlayerId}: {Reason}, inner: {Inner}",
                player.PlayerId, sendResult.Error.Message, sendResult.Inner);
        }
    }

    private static async Task ShowPreviousPlayerAction(
        ChasePigPlayerState previousPlayerOldState,
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (oldGameState.Progress == ChasePigGameProgress.Starting) return;
        
        switch (oldGameState.Progress)
        {
            case ChasePigGameProgress.Exposing:
                await SendPreviousPlayerExposeMessage(previousPlayerNewState, oldGameState,
                    newGameState, state.DeckService, state.Localizations, channelApi, logger);
                break;
            case ChasePigGameProgress.Progressing:
                await SendPreviousPlayerActionMessage(previousPlayerOldState,
                    previousPlayerNewState, oldGameState, newGameState, state,
                    channelApi, logger);
                break;
        }
    }

    private static async Task SendPreviousPlayerActionMessage(
        ChasePigPlayerState previousPlayerOldState,
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = state.Localizations.GetLocalization();

        var diff = previousPlayerOldState
            .Cards
            .Except(previousPlayerNewState.Cards)
            .ToImmutableArray();

        if (diff.IsEmpty) return;

        var playedCard = diff.Last();
        oldGameState.CardsOnTable.Add((previousPlayerNewState.PlayerId, playedCard));

        var title = localization
            .ChaseThePig
            .PlayMessage
            .Replace("{card}", playedCard.ToString());

        await using var renderedTable = await SkiaService.RenderCompetitiveTable(state.DeckService,
            oldGameState.CardsOnTable, oldGameState.Players.Values);

        foreach (var (_, playerState) in oldGameState.Players)
        {
            if (playerState.TextChannel == null) continue;
            
            var randomFileName = Utilities.GenerateRandomString() + ".jpg";
            var randomAttachmentUri = $"attachment://{randomFileName}";

            await using var imageCopy = new MemoryStream((int)renderedTable.Length);
            await renderedTable.CopyToAsync(imageCopy);
            renderedTable.Seek(0, SeekOrigin.Begin);
            imageCopy.Seek(0, SeekOrigin.Begin);

            var attachment = new OneOf<FileData, IPartialAttachment>[]
            {
                new FileData(randomFileName, imageCopy)
            };

            var isPreviousPlayer = playerState.PlayerId == previousPlayerNewState.PlayerId;
            title = title.Replace("{playerName}", isPreviousPlayer
                ? localization.GenericWords.Pronoun
                : previousPlayerNewState.PlayerName);

            var embed = new Embed
            {
                Author = new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                Colour = BurstColor.Burst.ToColor(),
                Title = title,
                Image = new EmbedImage(randomAttachmentUri),
                Thumbnail = new EmbedThumbnail(Constants.BurstLogo)
            };

            var sendResult = await channelApi
                .CreateMessageAsync(playerState.TextChannel.ID,
                    embeds: new[] { embed },
                    attachments: attachment);
            
            if (sendResult.IsSuccess) continue;

            logger.LogError("Failed to send previous player action message to {PlayerId}: {Reason}, inner: {Inner}",
                playerState.PlayerId, sendResult.Error.Message, sendResult.Inner);
        }

        if (oldGameState.CardsOnTable.Count != 4) return;
        
        oldGameState.CardsOnTable.Clear();

        foreach (var (_, playerState) in oldGameState.Players)
        {
            if (playerState.TextChannel == null) continue;
            
            var winner = newGameState
                .Players
                .First(p => p.Value.PlayerId == newGameState.PreviousWinner)
                .Value;
            var message = localization
                .ChaseThePig
                .WinTurnMessage
                .Replace("{playerName}", winner.PlayerName);
            var embed = new Embed
            {
                Author = new EmbedAuthor(message, IconUrl: winner.AvatarUrl),
                Colour = BurstColor.Burst.ToColor()
            };

            var sendResult = await channelApi
                .CreateMessageAsync(playerState.TextChannel.ID,
                    embeds: new[] { embed });
            if (sendResult.IsSuccess) continue;

            logger.LogError("Failed to send previous player action message to {PlayerId}: {Reason}, inner: {Inner}",
                playerState.PlayerId, sendResult.Error.Message, sendResult.Inner);
        }
    }

    private static async Task UpdateGameState(ChasePigGameState state, RawChasePigGameState? data, IDiscordRestGuildAPI guildApi)
    {
        if (data == null) return;

        state.Exposures = data.Exposures.ToImmutableArray();
        state.BaseBet = data.BaseBet;
        state.PreviousWinner = data.PreviousWinner;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;

        foreach (var (playerId, playerState) in data.Players)
        {
            if (state.Players.ContainsKey(playerId))
            {
                var player = state.Players[playerId];
                player.Cards = playerState.Cards.ToImmutableArray();
                player.Order = playerState.Order;
                player.Scores = playerState.Scores;
                player.AvatarUrl = playerState.AvatarUrl;
                player.CollectedCards = playerState.CollectedCards.ToImmutableArray();
                player.PlayerName = playerState.PlayerName;
                
                if (player.TextChannel != null || playerState.ChannelId == 0) continue;

                player.TextChannel =
                    await Utilities.TryGetTextChannel(state.Guilds, playerState.ChannelId, guildApi);
            }
            else
            {
                foreach (var guild in state.Guilds)
                {
                    var newPlayerState = await playerState.ToState(guildApi, guild);
                    state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
                    break;
                }
            }
        }
    }

    private static bool FilterExposureCards(Card card, IEnumerable<ChasePigExposure> exposures, RawChasePigGameState newGameState)
    {
        if (newGameState.PreviousWinner != 0) return true;

        var cardsToFilterOut = new List<Card>(4);
        foreach (var exposure in exposures)
        {
            switch (exposure)
            {
                case ChasePigExposure.Transformer:
                case ChasePigExposure.FirstTransformer:
                    cardsToFilterOut.Add(Card.Create(Suit.Club, 10));
                    break;
                case ChasePigExposure.DoubleGoat:
                case ChasePigExposure.FirstDoubleGoat:
                    cardsToFilterOut.Add(Card.Create(Suit.Diamond, 11));
                    break;
                case ChasePigExposure.DoubleMinus:
                case ChasePigExposure.FirstDoubleMinus:
                    cardsToFilterOut.Add(Card.Create(Suit.Heart, 1));
                    break;
                case ChasePigExposure.DoublePig:
                case ChasePigExposure.FirstDoublePig:
                    cardsToFilterOut.Add(Card.Create(Suit.Spade, 12));
                    break;
            }
        }

        return !cardsToFilterOut.Contains(card);
    }
}