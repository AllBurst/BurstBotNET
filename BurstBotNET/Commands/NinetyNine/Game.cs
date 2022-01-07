#pragma warning disable CA2252
using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Extensions.Embeds;
using Remora.Rest.Core;
using Remora.Rest.Results;
using Channel = System.Threading.Channels.Channel;
using Constants = BurstBotShared.Shared.Constants;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.NinetyNine;

using NinetyNineGame = IGame<NinetyNineGameState, RawNinetyNineGameState, NinetyNine, NinetyNinePlayerState, NinetyNineGameProgress, NinetyNineInGameRequestType>;
public partial class NinetyNine : NinetyNineGame
{
    private static readonly ImmutableArray<string> InGameRequestTypes =
    Enum.GetNames<NinetyNineInGameRequestType>()
        .ToImmutableArray();


    public static async Task<bool> HandleProgress(string messageContent, NinetyNineGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawNinetyNineGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult =
                    await HandleProgressChange(
                        deserializedIncomingData,
                        gameState, state, channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }
            await SendProgressMessages(gameState, deserializedIncomingData, state, channelApi, logger);
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);


        }
        catch (Exception ex)
        {

        }
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, NinetyNineGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static async Task AddPlayerState(string gameId, Snowflake guild, NinetyNinePlayerState playerState, GameStates gameStates)
    {
        var state = gameStates
           .NinetyNineGameStates
           .Item1
           .GetOrAdd(gameId, new NinetyNineGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel.ID.Value,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName
            })));
    }

    public static async Task StartListening(string gameId, State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var gameState = state.GameStates.NinetyNineGameStates.Item1
            .GetOrAdd(gameId, new NinetyNineGameState());
        logger.LogDebug("Chinese Poker game progress: {Progress}", gameState.Progress);

        await gameState.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        if (gameState.Progress != NinetyNineGameProgress.NotAvailable)
        {
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }

        gameState.Progress = NinetyNineGameProgress.Starting;
        gameState.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");

        var buffer = ArrayPool<byte>.Create(Constants.BufferSize, 1024);

        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession =
            await Game.GenericOpenWebSocketSession(GameName, state.Config, logger, cancellationTokenSource);
        gameState.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");

        var timeout = state.Config.Timeout;
        while (!gameState.Progress.Equals(NinetyNineGameProgress.Closed))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = NinetyNineGame.RunChannelTask(socketSession, gameState, InGameRequestTypes,
                NinetyNineInGameRequestType.Close, NinetyNineGameProgress.Closed, logger, cancellationTokenSource);

            var broadcastTask = NinetyNineGame.RunBroadcastTask(socketSession, gameState, buffer, state,
                channelApi,
                guildApi,
                logger, cancellationTokenSource);

            var timeoutTask = NinetyNineGame.RunTimeoutTask(timeout, gameState, NinetyNineGameProgress.Closed,
                logger,
                timeoutCancellationTokenSource);

            await await Task.WhenAny(channelTask, broadcastTask, timeoutTask);
            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
                logger.LogDebug("Timeout task cancelled");
                timeoutCancellationTokenSource.Dispose();
            });
        }

        await Game.GenericCloseGame(socketSession, logger, cancellationTokenSource);
        var retrieveResult =
            state.GameStates.ChinesePokerGameStates.Item1.TryGetValue(gameState.GameId, out var retrievedState);
        if (!retrieveResult)
            return;

        foreach (var (_, value) in retrievedState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.ID;

            var deleteResult = await channelApi
                .DeleteChannelAsync(channelId);
            if (!deleteResult.IsSuccess)
                logger.LogError("Failed to delete player's channel: {Reason}, inner: {Inner}",
                    deleteResult.Error.Message, deleteResult.Inner);

            state.GameStates.ChinesePokerGameStates.Item2.TryRemove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        gameState.Dispose();
        state.GameStates.ChinesePokerGameStates.Item1.Remove(gameState.GameId, out _);
        socketSession.Dispose();
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgressChange(RawNinetyNineGameState deserializedIncomingData, NinetyNineGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
    private static async Task SendProgressMessages(
     NinetyNineGameState gameState,
     RawNinetyNineGameState? deserializedIncomingData,
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
        logger.LogDebug("Game progress: {Progress}", gameState.Progress);

        switch (gameState.Progress)
        {
            case NinetyNineGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState!, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case NinetyNineGameProgress.Progressing:
            case NinetyNineGameProgress.Ending:
                {
                    getPlayerStateResult = deserializedIncomingData
                        .Players.TryGetValue(gameState.PreviousPlayerId, out var ppPlayerState);
                    if (!getPlayerStateResult)
                    {
                        logger.LogError("Failed to get second previous player {PlayerId}'s state",
                            gameState.PreviousPlayerId);
                        return;
                    }

                    var newDumpedCards = deserializedIncomingData.PreviousCard;

                    await ShowPreviousPlayerAction(previousPlayerNewState!, ppPlayerState!,
                        newDumpedCards, gameState, deserializedIncomingData, state, channelApi, logger);

                    if (deserializedIncomingData.Progress.Equals(NinetyNineGameProgress.Ending)) return;

                    await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                        state.Localizations, channelApi, logger);

                    break;
                }
        }
    }

    private static async Task SendInitialMessage(
    NinetyNinePlayerState? playerState,
    RawNinetyNinePlayerState newPlayerState,
    RawNinetyNineGameState deserializedIncomingData,
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
    private static async Task SendDrawingMessage(NinetyNineGameState gameState,
        RawNinetyNineGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
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
                        new SelectMenuComponent("Ninety_nine_draw", previousPlayerCards, localization.Draw, 1, 1)
                    }),
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.ShowHelp,
                            new PartialEmoji(Name: "❓"), "ninety_nine_help")
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
    NinetyNinePlayerState playerState,
    RawNinetyNinePlayerState nextPlayer,
    Localizations localizations)
    {
        var isCurrentPlayer = nextPlayer.PlayerId == playerState.PlayerId;
        var localization = localizations.GetLocalization();

        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
            : localization.GenericWords.PossessiveThird.Replace("{playerName}", nextPlayer.PlayerName);

        var title = localization.NinetyNine.TurnMessageTitle
            .Replace("{possessive}", possessive);

        var embed = new Embed(
            Author: new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Image: new EmbedImage(Constants.AttachmentUri));

        if (!isCurrentPlayer) return embed;

        embed = embed with
        {
            Description = $"{localization.NinetyNine.Cards}\n\n{string.Join('\n', nextPlayer.Cards)}"
        };

        return embed;
    }
    private static async Task ShowPreviousPlayerAction(
    RawNinetyNinePlayerState previousPlayerNewState,
    RawNinetyNinePlayerState previousPreviousPlayer,
    IEnumerable<Card> newDumpedCards,
    NinetyNineGameState oldGameState,
    NinetyNineGameState newGameState,
    State state,
    IDiscordRestChannelAPI channelApi,
    ILogger logger)
    {
        if (oldGameState.Progress.Equals(NinetyNineGameProgress.Starting)) return;

        var localization = state.Localizations.GetLocalization();
        var oldMaidLocalization = state.Localizations.GetLocalization().OldMaid;

        var dumpedCards = newDumpedCards.ToImmutableArray();

        var diff = dumpedCards
            .Except(oldGameState.PreviousCard)
            .ToImmutableArray();

        await using var renderedImage = diff.IsEmpty ? null : SkiaService.RenderDeck(state.DeckService, diff);
        await using var drawnCardBack = SkiaService.RenderCard(state.DeckService,
            newGameState.PreviousCard! with { IsFront = false });
        await using var drawnCardFront = SkiaService.RenderCard(state.DeckService,
            newGameState.PreviousCard!);

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
                        .Replace("{rank}", newGameState.PreviouslyDrawnCard!.Number.ToString())
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
    private static async Task UpdateGameState(
    NinetyNineGameState state,
    RawNinetyNineGameState? data,
    IDiscordRestGuildAPI guildApi)
    {
        if (data == null)
            return;
        state.CurrentTotal = data.CurrentTotal;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;
        state.PreviousCard = data.PreviousCard;
        state.BaseBet = data.BaseBet;

        foreach (var (playerId, playerState) in data.Players)
        {
            playerState.Cards.Sort((a, b) => a.Number.CompareTo(b.Number));
            if (state.Players.ContainsKey(playerId))
            {
                var oldPlayerState = state.Players.GetOrAdd(playerId, new NinetyNinePlayerState());
                oldPlayerState.Cards = playerState.Cards.ToImmutableArray()
                    .Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()));
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayerName = playerState.PlayerName;
                oldPlayerState.Order = playerState.Order; 

                if (playerState.ChannelId == 0 || oldPlayerState.TextChannel != null) continue;
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
                var newPlayerState = new NinetyNinePlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    Order = playerState.Order,
                    TextChannel = null
                };
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}
