using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Rest.Core;
using Remora.Rest.Results;
using Channel = System.Threading.Channels.Channel;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.OldMaid;

using OldMaidGame = IGame<OldMaidGameState, RawOldMaidGameState, OldMaid, OldMaidPlayerState, OldMaidGameProgress, OldMaidInGameRequestType>;

public partial class OldMaid : OldMaidGame
{
    public static async Task AddPlayerState(string gameId, Snowflake guild, OldMaidPlayerState playerState, GameStates gameStates)
    {
        var state = gameStates.OldMaidGameStates.Item1
            .GetOrAdd(gameId, new OldMaidGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.OldMaidGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new OldMaidInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel.ID.Value,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName,
                RequestType = OldMaidInGameRequestType.Deal
            })));
    }

    public static async Task<bool> HandleProgress(string messageContent, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData = JsonConvert
                .DeserializeObject<RawOldMaidGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null) return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData,
                    gameState, state, channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await SendProgressMessages(gameState, deserializedIncomingData, state, channelApi, logger);
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

    public static async Task<bool> HandleProgressChange(RawOldMaidGameState deserializedIncomingData, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result1 = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerNewState);
            var result2 =
                deserializedIncomingData.Players.TryGetValue(gameState.PreviousPlayerId, out var previousPreviousPlayer);
            if (result1 && result2)
            {
                await ShowPreviousPlayerAction(previousPlayerNewState!, previousPreviousPlayer!,
                    deserializedIncomingData.DumpedCards, gameState, deserializedIncomingData, state,
                    channelApi, logger);
            }
        }
        
        switch (deserializedIncomingData.Progress)
        {
            case OldMaidGameProgress.Ending:
                return true;
            case OldMaidGameProgress.Progressing:
                await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                break;
        }

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);
        
        return true;
    }

    public static async Task HandleEndingResult(string messageContent, OldMaidGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        if (state.Progress.Equals(OldMaidGameProgress.Ending)) return;
        
        logger.LogDebug("Handling ending result...");

        try
        {
            var endingData = JsonSerializer.Deserialize<OldMaidInGameResponseEndingData>(messageContent);
            if (endingData == null) return;

            state.Progress = OldMaidGameProgress.Ending;
            var winnerId = endingData
                .Rewards
                .MaxBy(pair => pair.Value)
                .Key;
            var winner = endingData.Players[winnerId];

            var localization = localizations.GetLocalization().OldMaid;

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
                JsonSerializer.SerializeToUtf8Bytes(new OldMaidInGameRequest
                {
                    GameId = state.GameId,
                    RequestType = OldMaidInGameRequestType.Close,
                    PlayerId = 0
                })));
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(messageContent))
                return;
            logger.LogError("An exception occurred when handling ending result: {Exception}", ex);
            logger.LogError("Exception message: {Message}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
        }
    }

    private static async Task SendProgressMessages(
        OldMaidGameState gameState,
        RawOldMaidGameState? deserializedIncomingData,
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
            case OldMaidGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState!, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case OldMaidGameProgress.Progressing:
            case OldMaidGameProgress.Ending:
            {
                getPlayerStateResult = deserializedIncomingData
                    .Players.TryGetValue(gameState.PreviousPlayerId, out var ppPlayerState);
                if (!getPlayerStateResult)
                {
                    logger.LogError("Failed to get second previous player {PlayerId}'s state",
                        gameState.PreviousPlayerId);
                    return;
                }
        
                var newDumpedCards = deserializedIncomingData.DumpedCards;

                await ShowPreviousPlayerAction(previousPlayerNewState!, ppPlayerState!,
                    newDumpedCards, gameState, deserializedIncomingData, state, channelApi, logger);

                if (deserializedIncomingData.Progress.Equals(OldMaidGameProgress.Ending)) return;

                await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                
                break;
            }
        }
    }

    private static async Task SendInitialMessage(
        OldMaidPlayerState? playerState,
        RawOldMaidPlayerState newPlayerState,
        RawOldMaidGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().OldMaid;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);

        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{helpText}", localization.CommandList["draw"])
            .Replace("{cardNames}", cardNames);
        
        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);

        var embed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

        var attachment = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedDeck))
        };

        var sendResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { embed },
                attachments: attachment);
        if (!sendResult.IsSuccess)
            logger.LogError("Failed to send initial message to player {PlayerId}: {Reason}, inner: {Inner}",
                newPlayerState.PlayerId,
                sendResult.Error.Message, sendResult.Inner);
    }

    private static async Task SendDrawingMessage(
        OldMaidGameState gameState,
        RawOldMaidGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
        )
    {
        var localization = localizations.GetLocalization().OldMaid;
        
        var nextPlayer = deserializedIncomingData
            .Players
            .First(pair => pair.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        await using var nextPlayerDeck = SkiaService.RenderDeck(deckService, nextPlayer
            .Cards
            .Select(c => c with { IsFront = false }));

        foreach (var (playerId, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null) continue;

            var embed = BuildDrawingMessage(playerState, nextPlayer, localizations);

            if (nextPlayer.PlayerId == playerId)
            {
                await using var renderedImage = SkiaService.RenderDeck(deckService, nextPlayer.Cards);
                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedImage))
                };

                var previousPlayer = deserializedIncomingData
                    .Players
                    .First(p => p.Value.PlayerId == deserializedIncomingData.PreviousPlayerId)
                    .Value;

                var previousPlayerCards = previousPlayer
                    .Cards
                    .Select((_, i) => new SelectOption($"Card {i + 1}", i.ToString(CultureInfo.InvariantCulture),
                        localizations.GetLocalization().GenericWords.From
                            .Replace("{player}", previousPlayer.PlayerName), new PartialEmoji(Name: "🎴")))
                    .ToImmutableArray();

                var components = new IMessageComponent[]
                {
                    new ActionRowComponent(new []
                    {
                        new SelectMenuComponent("old_maid_draw", previousPlayerCards, localization.Draw, 1, 1)
                    }),
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.ShowHelp,
                            new PartialEmoji(Name: "❓"), "old_maid_help")
                    })
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment,
                        components: components);
                if (!sendResult.IsSuccess)
                    logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                        playerId, sendResult.Error.Message, sendResult.Inner);
            }
            else
            {
                await using var imageCopy = new MemoryStream((int)nextPlayerDeck.Length);
                await nextPlayerDeck.CopyToAsync(imageCopy);
                nextPlayerDeck.Seek(0, SeekOrigin.Begin);
                imageCopy.Seek(0, SeekOrigin.Begin);
                
                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, imageCopy))
                };
                
                var sendResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment);
                if (!sendResult.IsSuccess)
                    logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                        playerId, sendResult.Error.Message, sendResult.Inner);
            }
        }
    }

    private static Embed BuildDrawingMessage(
        OldMaidPlayerState playerState,
        RawOldMaidPlayerState nextPlayer,
        Localizations localizations)
    {
        var isNextPlayer = nextPlayer.PlayerId == playerState.PlayerId;
        var localization = localizations.GetLocalization();

        var possessive = isNextPlayer
            ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
            : localization.GenericWords.PossessiveThird.Replace("{playerName}", nextPlayer.PlayerName);

        var title = localization.OldMaid.TurnMessageTitle
            .Replace("{possessive}", possessive);

        var embed = new Embed(
            Author: new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Image: new EmbedImage(Constants.AttachmentUri));

        if (!isNextPlayer) return embed;

        embed = embed with
        {
            Description = $"{localization.OldMaid.Cards}\n\n{string.Join('\n', nextPlayer.Cards)}"
        };

        return embed;
    }

    private static async Task ShowPreviousPlayerAction(
        RawOldMaidPlayerState previousPlayerNewState,
        RawOldMaidPlayerState previousPreviousPlayer,
        IEnumerable<Card> newDumpedCards,
        OldMaidGameState oldGameState,
        RawOldMaidGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (oldGameState.Progress.Equals(OldMaidGameProgress.Starting)) return;
        
        var localization = state.Localizations.GetLocalization();
        var oldMaidLocalization = state.Localizations.GetLocalization().OldMaid;

        var dumpedCards = newDumpedCards.ToImmutableArray();
        
        var diff = dumpedCards
            .Except(oldGameState.DumpedCards)
            .ToImmutableArray();

        await using var renderedImage = diff.IsEmpty ? null : SkiaService.RenderDeck(state.DeckService, diff);
        await using var drawnCardBack = SkiaService.RenderCard(state.DeckService,
            newGameState.PreviouslyDrawnCard!.Value with { IsFront = false });
        await using var drawnCardFront = SkiaService.RenderCard(state.DeckService,
            newGameState.PreviouslyDrawnCard!.Value);

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;
            
            var isPreviousPlayer = player.Order == oldGameState.CurrentPlayerOrder;
            var isPpPlayer = player.PlayerId == previousPreviousPlayer.PlayerId;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : previousPlayerNewState.PlayerName;

            var title = oldMaidLocalization
                .DrawMessage
                .Replace("{previousPlayerName}", pronoun)
                .Replace("{ppPlayerName}",
                    isPpPlayer ? localization.GenericWords.Pronoun : previousPreviousPlayer.PlayerName)
                .Replace("{card}",
                    !isPreviousPlayer && !isPpPlayer ? "card" : newGameState.PreviouslyDrawnCard!.ToString());

            if (!diff.IsEmpty)
            {
                var embed = new Embed(
                    Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                    Title: title,
                    Colour: BurstColor.Burst.ToColor(),
                    Image: new EmbedImage(Constants.AttachmentUri),
                    Description: oldMaidLocalization.ThrowMessage
                        .Replace("{previousPlayerName}", pronoun)
                        .Replace("{rank}", newGameState.PreviouslyDrawnCard!.Value.Number.ToString())
                );
                
                await using var streamCopy = new MemoryStream((int)renderedImage!.Length);
                await renderedImage.CopyToAsync(streamCopy);
                streamCopy.Seek(0, SeekOrigin.Begin);
                renderedImage.Seek(0, SeekOrigin.Begin);
                
                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, streamCopy))
                };
                
                var sentResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment);

                if (sentResult.IsSuccess) continue;
                
                var innerError = sentResult.Error as RestResultError<RestError>;
                logger.LogError("Failed to show previous player's action with picture: {Reason}, inner: {Inner}",
                    sentResult.Error.Message, sentResult.Inner);
                logger.LogError("Rest error: {Error}, message: {Message}", innerError?.Error, innerError?.Message);
            }
            else
            {
                var embed = new Embed(
                    Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                    Title: title,
                    Colour: BurstColor.Burst.ToColor(),
                    Image: new EmbedImage(Constants.AttachmentUri)
                );

                await using var imageCopy =
                    new MemoryStream(isPreviousPlayer ? (int)drawnCardFront.Length : (int)drawnCardBack.Length);
                if (isPreviousPlayer || isPpPlayer)
                {
                    await drawnCardFront.CopyToAsync(imageCopy);
                    drawnCardFront.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    await drawnCardBack.CopyToAsync(imageCopy);
                    drawnCardBack.Seek(0, SeekOrigin.Begin);
                }

                imageCopy.Seek(0, SeekOrigin.Begin);

                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, imageCopy))
                };

                var sentResult = await channelApi
                    .CreateMessageAsync(player.TextChannel!.ID,
                        embeds: new[] { embed },
                        attachments: attachment);

                if (sentResult.IsSuccess) continue;
                
                var innerError = sentResult.Error as RestResultError<RestError>;
                logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                    sentResult.Error.Message, sentResult.Inner);
                logger.LogError("Rest error: {Error}, message: {Message}", innerError?.Error, innerError?.Message);
            }
        }
    }

    private static async Task UpdateGameState(OldMaidGameState state, RawOldMaidGameState? data,
        IDiscordRestGuildAPI guildApi)
    {
        if (data == null) return;

        state.BaseBet = data.BaseBet;
        state.DumpedCards = data.DumpedCards.ToImmutableArray();
        state.TotalBet = data.TotalBet;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;

        foreach (var (playerId, playerState) in data.Players)
        {
            if (state.Players.ContainsKey(playerId))
            {
                var oldPlayerState = state.Players.GetOrAdd(playerId, new OldMaidPlayerState());

                oldPlayerState.Cards = playerState.Cards.ToImmutableArray();
                oldPlayerState.Order = playerState.Order;
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayerName = playerState.PlayerName;

                if (oldPlayerState.TextChannel != null || playerState.ChannelId == 0) continue;

                foreach (var guild in state.Guilds)
                {
                    var getChannelsResult = await guildApi
                        .GetGuildChannelsAsync(guild);
                    if (!getChannelsResult.IsSuccess) continue;
                    var channels = getChannelsResult.Entity;
                    if (!channels.Any()) continue;
                    var textChannel = channels
                        .FirstOrDefault(c => c.ID.Value == playerState.ChannelId);
                    if (textChannel == null) continue;
                    oldPlayerState.TextChannel = textChannel;
                }
            }
            else
            {
                var newPlayerState = new OldMaidPlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    Order = playerState.Order,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null
                };

                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}