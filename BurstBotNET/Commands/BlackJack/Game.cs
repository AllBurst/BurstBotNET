using System.Buffers;
using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.BlackJack;

using BlackJackGame = IGame<BlackJackGameState, RawBlackJackGameState, BlackJack, BlackJackGameProgress, BlackJackInGameRequestType>;

#pragma warning disable CA2252
public partial class BlackJack : BlackJackGame
{
    private static readonly ImmutableArray<string> InGameRequestTypes = Enum
        .GetNames<BlackJackInGameRequestType>()
        .ToImmutableArray();

    private static async Task StartListening(
        string gameId,
        State state,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var gameState = state.GameStates.BlackJackGameStates.Item1
            .GetOrAdd(gameId, new BlackJackGameState());
        logger.LogDebug("Game progress: {Progress}", gameState.Progress);

        await gameState.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        if (gameState.Progress != BlackJackGameProgress.NotAvailable)
        {
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }

        gameState.Progress = BlackJackGameProgress.Starting;
        gameState.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");

        var buffer = ArrayPool<byte>.Create(Constants.BufferSize, 1024);

        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession = await Game.GenericOpenWebSocketSession(GameName, state.Config, logger, cancellationTokenSource);
        gameState.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");

        var timeout = state.Config.Timeout;

        while (!gameState.Progress.Equals(BlackJackGameProgress.Closed))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = BlackJackGame.RunChannelTask(socketSession,
                gameState,
                InGameRequestTypes,
                BlackJackInGameRequestType.Close,
                BlackJackGameProgress.Closed,
                logger,
                cancellationTokenSource);

            var broadcastTask = BlackJackGame.RunBroadcastTask(socketSession,
                gameState,
                buffer,
                state,
                logger,
                cancellationTokenSource);

            var timeoutTask = BlackJackGame.RunTimeoutTask(timeout, gameState, BlackJackGameProgress.Closed, logger,
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
        var retrieveResult = state.GameStates.BlackJackGameStates.Item1.TryGetValue(gameState.GameId, out var retrievedState);
        if (!retrieveResult)
            return;

        foreach (var (_, value) in retrievedState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.Id;
            await value.TextChannel.DeleteAsync();
            state.GameStates.BlackJackGameStates.Item2.Remove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        state.GameStates.BlackJackGameStates.Item1.Remove(gameState.GameId, out _);
        socketSession.Dispose();
    }

    private static async Task AddBlackJackPlayerState(string gameId,
        DiscordGuild guild,
        BlackJackPlayerState playerState,
        GameStates gameStates)
    {
        var state = gameStates.BlackJackGameStates.Item1.GetOrAdd(gameId, new BlackJackGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.BlackJackGameStates.Item2.Add(playerState.TextChannel.Id);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new BlackJackInGameRequest
            {
                GameId = gameId,
                AvatarUrl = playerState.AvatarUrl,
                PlayerId = playerState.PlayerId,
                ChannelId = playerState.TextChannel.Id,
                PlayerName = playerState.PlayerName,
                OwnTips = playerState.OwnTips,
                ClientType = ClientType.Discord,
                RequestType = BlackJackInGameRequestType.Deal
            })
        ));
    }

    public static async Task HandleBlackJackMessage(
        DiscordClient client,
        MessageCreateEventArgs e,
        GameStates gameStates,
        ulong channelId,
        Localizations localizations)
    {
        var state = gameStates
            .BlackJackGameStates
            .Item1
            .Where(pair => !pair.Value.Players
                .Where(p => p.Value.TextChannel?.Id == channelId)
                .ToImmutableArray().IsEmpty)
            .Select(p => p.Value)
            .First();

        var playerState = state
            .Players
            .Where(p => p.Value.TextChannel?.Id == channelId)
            .Select(p => p.Value)
            .First();

        // Do not respond if the one who's typing is not the owner of the channel.
        if (e.Message.Author.Id != playerState.PlayerId)
            return;

        var channel = e.Message.Channel;
        var splitContent = e.Message.Content
            .ToLowerInvariant()
            .Trim()
            .Split(' ')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToImmutableArray();
        var helpExecuted = await Help.Help.GenericCommandHelp(channel, splitContent, localizations.GetLocalization().BlackJack);

        if (helpExecuted)
            return;

        if (state.CurrentPlayerOrder != playerState.Order)
            return;

        switch (state.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                switch (splitContent[0])
                {
                    case "draw":
                        await SendGenericData(state, playerState,
                            BlackJackInGameRequestType.Draw);
                        break;
                    case "stand":
                        await SendGenericData(state, playerState,
                            BlackJackInGameRequestType.Stand);
                        break;
                }

                break;
            }
            case BlackJackGameProgress.Gambling:
            {
                switch (splitContent[0])
                {
                    case "fold":
                        await SendGenericData(state, playerState, BlackJackInGameRequestType.Fold);
                        break;
                    case "call":
                        await SendGenericData(state, playerState, BlackJackInGameRequestType.Call);
                        break;
                    case "allin":
                    {
                        var remainingTips = playerState.OwnTips - playerState.BetTips - state.HighestBet;
                        await SendRaiseData(state, playerState, (int)remainingTips);
                        break;
                    }
                    case "raise":
                    {
                        try
                        {
                            var raiseBet = int.Parse(splitContent[1]);
                            if (playerState.BetTips + raiseBet > playerState.OwnTips)
                            {
                                await e.Message.Channel.SendMessageAsync(
                                    localizations.GetLocalization().BlackJack.RaiseExcessNumber);
                                return;
                            }

                            await SendRaiseData(state, playerState, raiseBet);
                            break;
                        }
                        catch (Exception ex)
                        {
                            client.Logger.LogError("An exception occurred when handling message: {Exception}",
                                ex.Message);
                            await e.Message.Channel.SendMessageAsync(
                                localizations.GetLocalization().BlackJack.RaiseInvalidNumber);
                            return;
                        }
                    }
                }

                break;
            }
            case BlackJackGameProgress.NotAvailable:
            case BlackJackGameProgress.Starting:
            case BlackJackGameProgress.Ending:
            case BlackJackGameProgress.Closed:
            default:
                break;
        }
    }

    public static async Task<bool> HandleProgress(
        string messageContent,
        BlackJackGameState gameState,
        State state,
        ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawBlackJackGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult =
                    await HandleProgressChange(deserializedIncomingData, gameState, state.GameStates,
                        state.DeckService, state.Localizations);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            var previousHighestBet = gameState.HighestBet;
            await UpdateGameState(gameState, deserializedIncomingData);
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result = gameState.Players.TryGetValue(playerId, out var previousPlayerState);
            if (!result)
            {
                gameState.Semaphore.Release();
                return false;
            }

            result = Enum.TryParse<BlackJackInGameRequestType>(deserializedIncomingData.PreviousRequestType,
                out var previousRequestType);
            if (!result)
            {
                gameState.Semaphore.Release();
                return false;
            }

            await SendProgressMessages(gameState, previousPlayerState, previousRequestType, previousHighestBet,
                deserializedIncomingData, state.DeckService, state.Localizations, logger);

            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released after sending progress messages");
        }
        catch (JsonSerializationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("An exception occurred when handling progress: {Exception}", ex);
            logger.LogError("Exception message: {Message}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in an exception");
            return false;
        }

        return true;
    }

    private static async Task<bool> HandleProgressChange(
        RawBlackJackGameState deserializedIncomingData,
        BlackJackGameState state,
        GameStates gameStates,
        DeckService deckService,
        Localizations localizations)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerState);
            if (result)
            {
                result = Enum.TryParse<BlackJackInGameRequestType>(deserializedIncomingData.PreviousRequestType,
                    out var previousRequestType);
                if (result)
                    await SendPreviousPlayerActionMessage(state, previousPlayerState!,
                        previousRequestType, deckService, localizations, state.HighestBet);
            }
        }

        if (deserializedIncomingData.Progress.Equals(BlackJackGameProgress.Ending))
            return true;

        state.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(state, deserializedIncomingData);
        var firstPlayer = state.Players
            .First(pair => pair.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        switch (deserializedIncomingData.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                foreach (var playerState in state.Players)
                {
                    if (playerState.Value.TextChannel == null)
                        continue;

                    gameStates.BlackJackGameStates.Item2.Add(playerState.Value.TextChannel.Id);
                    var embed = BuildTurnMessage(
                        playerState,
                        deserializedIncomingData.CurrentPlayerOrder,
                        firstPlayer,
                        state,
                        deckService,
                        localizations
                    );
                    await playerState.Value.TextChannel.SendMessageAsync(embed);
                }

                break;
            }
            case BlackJackGameProgress.Gambling:
            {
                foreach (var playerState in state.Players)
                {
                    if (playerState.Value.TextChannel == null)
                        continue;

                    await playerState.Value.TextChannel
                        .SendMessageAsync(localizations.GetLocalization().BlackJack
                            .GamblingInitialMessage);
                    var embed = BuildTurnMessage(
                        playerState,
                        deserializedIncomingData.CurrentPlayerOrder,
                        firstPlayer,
                        state,
                        deckService,
                        localizations
                    );
                    await playerState.Value.TextChannel.SendMessageAsync(embed);
                }

                break;
            }
        }

        return true;
    }

    public static async Task HandleEndingResult(
        string messageContent,
        BlackJackGameState state,
        Localizations localizations,
        DeckService deckService,
        ILogger logger)
    {
        if (state.Progress.Equals(BlackJackGameProgress.Ending))
            return;

        logger.LogDebug("Handling ending result...");
        
        try
        {
            var deserializedEndingData = JsonSerializer.Deserialize<BlackJackInGameResponseEndingData>(messageContent);
            if (deserializedEndingData == null)
                return;

            state.Progress = deserializedEndingData.Progress;
            var result = state.Players.TryGetValue(deserializedEndingData.Winner?.PlayerId ?? 0, out var winner);
            if (!result)
                return;

            var localization = localizations.GetLocalization().BlackJack;

            var description = localization.WinDescription
                .Replace("{playerName}", winner!.PlayerName)
                .Replace("{totalRewards}", deserializedEndingData.TotalRewards.ToString());

            var embed = new DiscordEmbedBuilder()
                .WithColor((int)BurstColor.Burst)
                .WithTitle(localization.WinTitle.Replace("{playerName}", winner.PlayerName))
                .WithDescription(description)
                .WithImageUrl(winner.AvatarUrl);

            foreach (var (_, playerState) in deserializedEndingData.Players)
            {
                var cardNames = string.Join('\n', playerState.Cards.Select(c => c.ToString()));
                var totalPoints = playerState.Cards.GetRealizedValues(100);
                embed = embed.AddField(
                    playerState.PlayerName,
                    localization.TotalPointsMessage.Replace("{cardNames}", cardNames)
                        .Replace("{totalPoints}", totalPoints.ToString()), true);
            }

            foreach (var (_, playerState) in state.Players)
            {
                if (playerState.TextChannel == null)
                    continue;

                await playerState.TextChannel.SendMessageAsync(embed);
            }

            await state.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(0, JsonSerializer.SerializeToUtf8Bytes(
                new BlackJackInGameRequest
                {
                    RequestType = BlackJackInGameRequestType.Close,
                    GameId = state.GameId,
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

    private static async Task SendGenericData(BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        BlackJackInGameRequestType requestType)
    {
        var sendData = new Tuple<ulong, byte[]>(playerState.PlayerId, JsonSerializer.SerializeToUtf8Bytes(
            new BlackJackInGameRequest
            {
                RequestType = requestType,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId
            }));
        await gameState.Channel!.Writer.WriteAsync(sendData);
    }

    private static async Task SendRaiseData(
        BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        int raiseBet)
    {
        var sendData = new Tuple<ulong, byte[]>(playerState.PlayerId, JsonSerializer.SerializeToUtf8Bytes(
            new BlackJackInGameRequest
            {
                RequestType = BlackJackInGameRequestType.Raise,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                Bets = raiseBet
            }));
        await gameState.Channel!.Writer.WriteAsync(sendData);
    }

    private static async Task SendProgressMessages(
        BlackJackGameState state,
        BlackJackPlayerState? previousPlayerState,
        BlackJackInGameRequestType previousRequestType,
        int previousHighestBet,
        RawBlackJackGameState? deserializedStateData,
        DeckService deckService,
        Localizations localizations,
        ILogger logger
    )
    {
        if (deserializedStateData == null)
            return;

        logger.LogDebug("Sending progress messages...");

        switch (state.Progress)
        {
            case BlackJackGameProgress.Starting:
                await SendInitialMessage(previousPlayerState, deckService, localizations, logger);
                break;
            case BlackJackGameProgress.Progressing:
                await SendDrawingMessage(
                    state,
                    previousPlayerState,
                    state.CurrentPlayerOrder,
                    previousRequestType,
                    deserializedStateData.Progress,
                    deckService,
                    localizations
                );
                break;
            case BlackJackGameProgress.Gambling:
                await SendGamblingMessage(
                    state, previousPlayerState, state.CurrentPlayerOrder, previousRequestType, previousHighestBet,
                    deserializedStateData.Progress, deckService, localizations);
                break;
        }
    }

    private static async Task SendInitialMessage(
        BlackJackPlayerState? playerState,
        DeckService deckService,
        Localizations localizations,
        ILogger logger)
    {
        if (playerState == null || playerState.TextChannel == null)
            return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().BlackJack;
        var prefix = localization.InitialMessagePrefix;
        var postfix = localization.InitialMessagePostfix
            .Replace("{cardPoints}", playerState.Cards.GetRealizedValues(100).ToString());
        var cardNames = prefix +
                        string.Join('\n', playerState.Cards.Select(c => c.IsFront ? c.ToString() : $"**{c}**")) +
                        postfix;

        var description = localization.InitialMessageDescription
            .Replace("{cardsNames}", cardNames);

        await playerState.TextChannel.SendMessageAsync(new DiscordMessageBuilder()
            .WithEmbed(new DiscordEmbedBuilder()
                .WithAuthor(playerState.PlayerName, iconUrl: playerState.AvatarUrl)
                .WithColor((int)BurstColor.Burst)
                .WithTitle(localization.InitialMessageTitle)
                .WithDescription(description)
                .WithFooter(localization.InitialMessageFooter)
                .WithThumbnail(Constants.BurstLogo)
                .WithImageUrl(Constants.AttachmentUri))
            .WithFile(Constants.OutputFileName, SkiaService.RenderDeck(deckService,
                playerState.Cards.Select(c => c with { IsFront = true }))));
    }

    private static async Task SendDrawingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? previousPlayerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType previousRequestType,
        BlackJackGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations
    )
    {
        if (previousPlayerState == null)
            return;

        await SendPreviousPlayerActionMessage(gameState,
            ((IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>)previousPlayerState).ToRaw(),
            previousRequestType,
            deckService, localizations);

        if (!gameState.Progress.Equals(nextProgress))
            return;

        var nextPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == currentPlayerOrder)
            .Value;

        foreach (var state in gameState.Players)
        {
            if (state.Value.TextChannel == null)
                continue;

            var embed = BuildTurnMessage(state, currentPlayerOrder, nextPlayer, gameState,
                deckService, localizations);
            await state.Value.TextChannel.SendMessageAsync(embed);
        }
    }

    private static async Task SendGamblingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? previousPlayerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType previousRequestType,
        int previousHighestBet,
        BlackJackGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations
    )
    {
        if (previousPlayerState == null)
            return;

        await SendPreviousPlayerActionMessage(gameState, ((IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>)previousPlayerState).ToRaw(), previousRequestType,
            deckService, localizations, previousHighestBet);

        if (gameState.Progress != nextProgress)
            return;

        var currentPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == currentPlayerOrder)
            .Value;
        foreach (var state in gameState.Players)
        {
            if (state.Value.TextChannel == null)
                continue;

            var embed = BuildTurnMessage(state, currentPlayerOrder, currentPlayer, gameState,
                deckService, localizations);
            await state.Value.TextChannel.SendMessageAsync(embed);
        }
    }

    private static async Task SendPreviousPlayerActionMessage(
        BlackJackGameState gameState,
        RawBlackJackPlayerState previousPlayerState,
        BlackJackInGameRequestType previousRequestType,
        DeckService deck,
        Localizations localizations,
        int? previousHighestBet = null)
    {
        var previousPlayerOrder = previousPlayerState.Order;
        var localization = localizations.GetLocalization();
        var lastCard = previousPlayerState.Cards.Last();
        var lastCardImage = SkiaService.RenderCard(deck, lastCard);

        foreach (var (_, state) in gameState.Players)
        {
            if (state.TextChannel == null)
                continue;

            var isPreviousPlayer = previousPlayerOrder == state.Order;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : previousPlayerState.PlayerName;
            var currentPoints = previousPlayerState.Cards.GetRealizedValues(100);

            switch (gameState.Progress)
            {
                case BlackJackGameProgress.Progressing:
                {
                    var authorText = BuildPlayerActionMessage(localizations, previousRequestType, pronoun, lastCard);

                    var embed = new DiscordEmbedBuilder()
                        .WithAuthor(authorText, iconUrl: previousPlayerState.AvatarUrl)
                        .WithColor((int)BurstColor.Burst);

                    var message = new DiscordMessageBuilder();

                    if (previousRequestType.Equals(BlackJackInGameRequestType.Draw))
                    {
                        embed = embed.WithImageUrl(Constants.AttachmentUri);
                        message = message.WithFile(Constants.OutputFileName, lastCardImage, true);
                    }

                    if (isPreviousPlayer)
                        embed = embed
                            .WithDescription(
                                localization.BlackJack.CardPoints.Replace("{cardPoints}", currentPoints.ToString()));

                    await state.TextChannel.SendMessageAsync(message.WithEmbed(embed));
                    break;
                }
                case BlackJackGameProgress.Gambling:
                {
                    var verb = isPreviousPlayer
                        ? localization.GenericWords.ParticipateSecond
                        : localization.GenericWords.ParticipateThird;

                    var authorText = BuildPlayerActionMessage(
                        localizations, previousRequestType, pronoun,
                        highestBet: gameState.HighestBet,
                        verb: verb,
                        diff: gameState.HighestBet - previousHighestBet!.Value);

                    var embed = new DiscordEmbedBuilder()
                        .WithAuthor(authorText, null, previousPlayerState.AvatarUrl)
                        .WithColor((int)BurstColor.Burst);

                    if (isPreviousPlayer)
                        embed = embed.WithDescription(
                            localization.BlackJack.CardPoints.Replace(
                                "{cardPoints}",
                                currentPoints.ToString()
                            )
                        );

                    await state.TextChannel.SendMessageAsync(embed);
                    break;
                }
            }
        }
    }

    private static string BuildPlayerActionMessage(
        Localizations localizations,
        BlackJackInGameRequestType requestType,
        string playerName,
        Card? lastCard = null,
        int? highestBet = null,
        string? verb = null,
        int? diff = null
    )
    {
        var localization = localizations.GetLocalization().BlackJack;
        return requestType switch
        {
            BlackJackInGameRequestType.Draw => localization.Draw
                .Replace("{playerName}", playerName)
                .Replace("{lastCard}", lastCard!.ToStringSimple()),
            BlackJackInGameRequestType.Stand => localization.Stand
                .Replace("{playerName}", playerName),
            BlackJackInGameRequestType.Call => localization.Call
                .Replace("{playerName}", playerName)
                .Replace("{highestBet}", highestBet.ToString()),
            BlackJackInGameRequestType.Fold => localization.Fold
                .Replace("{playerName}", playerName)
                .Replace("{verb}", verb),
            BlackJackInGameRequestType.Raise => localization.Raise
                .Replace("{playerName}", playerName)
                .Replace("{diff}", diff.ToString())
                .Replace("{highestBet}", highestBet.ToString()),
            BlackJackInGameRequestType.AllIn => localization.Allin
                .Replace("{playerName}", playerName)
                .Replace("{highestBet}", highestBet.ToString()),
            _ => localization.Unknown
                .Replace("{playerName}", playerName)
        };
    }

    private static DiscordMessageBuilder BuildTurnMessage(
        KeyValuePair<ulong, BlackJackPlayerState> entry,
        int currentPlayerOrder,
        BlackJackPlayerState currentPlayer,
        BlackJackGameState gameState,
        DeckService deckService,
        Localizations localizations)
    {
        var localization = localizations.GetLocalization();
        var state = entry.Value;
        var isCurrentPlayer = state.Order == currentPlayerOrder;

        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond
            : localization.GenericWords.PossessiveThird
                .Replace("{playerName}", currentPlayer.PlayerName);

        var cardNames = $"{possessive}{localization.GenericWords.Card}:\n" + string.Join('\n', currentPlayer.Cards
            .Where(c => isCurrentPlayer || c.IsFront)
            .Select(c => c.IsFront ? c.ToString() : $"**{c}**"));

        var title = localization.BlackJack.TurnMessageTitle
            .Replace("{possessive}", isCurrentPlayer ? possessive.ToLowerInvariant() : possessive);

        var description = cardNames + (isCurrentPlayer
            ? "\n\n" + localization.BlackJack.CardPoints
                .Replace("{cardPoints}", state.Cards.GetRealizedValues(100).ToString())
            : "");

        var embed = new DiscordEmbedBuilder()
            .WithAuthor(currentPlayer.PlayerName, iconUrl: currentPlayer.AvatarUrl)
            .WithColor((int)BurstColor.Burst)
            .WithTitle(title)
            .WithImageUrl(Constants.AttachmentUri);

        var messageBuilder = new DiscordMessageBuilder()
            .WithFile(Constants.OutputFileName, SkiaService.RenderDeck(deckService,
                currentPlayer.Cards.Select(c => isCurrentPlayer ? c with { IsFront = true } : c)));

        switch (gameState.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                if (isCurrentPlayer) embed = embed.WithFooter(localization.BlackJack.ProgressingFooter);

                embed = embed.WithDescription(description);
                break;
            }
            case BlackJackGameProgress.Gambling:
            {
                var additionalDescription =
                    description + (isCurrentPlayer ? localization.BlackJack.TurnMessageDescription : "");
                embed = embed
                    .AddField(localization.BlackJack.HighestBets, gameState.HighestBet.ToString(), true)
                    .AddField(localization.BlackJack.YourBets, state.BetTips.ToString(), true)
                    .AddField(localization.BlackJack.TipsBeforeGame, state.OwnTips.ToString())
                    .WithDescription(additionalDescription);
                break;
            }
        }

        return messageBuilder.WithEmbed(embed);
    }

    private static async Task UpdateGameState(BlackJackGameState state, RawBlackJackGameState? data)
    {
        if (data == null)
            return;

        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.CurrentTurn = data.CurrentTurn;
        state.HighestBet = data.HighestBet;
        state.PreviousPlayerId = data.PreviousPlayerId;
        state.PreviousRequestType = data.PreviousRequestType;

        foreach (var (playerId, playerState) in data.Players)
            if (state.Players.ContainsKey(playerId))
            {
                var player = state.Players[playerId];
                player.BetTips = playerState.BetTips;
                player.Cards = playerState.Cards;
                player.Order = playerState.Order;
                player.PlayerName = playerState.PlayerName;
                player.OwnTips = playerState.OwnTips;
                player.AvatarUrl = playerState.AvatarUrl;

                if (playerState.ChannelId == 0 || player.TextChannel != null) continue;
                foreach (var guild in state.Guilds)
                {
                    var channels = await guild
                        .GetChannelsAsync();
                    if (channels == null || !channels.Any())
                        continue;
                    var textChannel = channels.FirstOrDefault(c => c.Id == playerState.ChannelId);
                    if (textChannel == null)
                        continue;
                    player.TextChannel = textChannel;
                }
            }
            else
            {
                var newPlayerState = new BlackJackPlayerState
                {
                    GameId = playerState.GameId,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null,
                    OwnTips = playerState.OwnTips,
                    BetTips = playerState.BetTips,
                    Order = playerState.Order,
                    Cards = playerState.Cards,
                    AvatarUrl = playerState.AvatarUrl
                };
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
    }
}