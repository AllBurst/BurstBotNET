using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Extensions;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Game.BlackJack;
using BurstBotNET.Shared.Models.Game.BlackJack.Serializables;
using BurstBotNET.Shared.Models.Game.Serializables;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.BlackJack;

public partial class BlackJack
{
    private enum SocketOperation
    {
        Continue, Shutdown, Close
    }

    private const int DefaultBufferSize = 4096;
    private static readonly ImmutableList<string> InGameRequestTypes = Enum
        .GetNames<BlackJackInGameRequestType>()
        .ToImmutableList();
    //private readonly ArrayPool<byte> _channelBuffer = ArrayPool<byte>.Create();
    private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

    public async Task StartListening(
        string gameId,
        Config config,
        GameStates gameStates,
        DiscordGuild guild,
        Localizations localizations,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;
        
        var state = gameStates.BlackJackGameStates.Item1
            .GetOrAdd(gameId, new BlackJackGameState());
        logger.LogDebug("Game progress: {Progress}", state.Progress);

        if (state.Progress != BlackJackGameProgress.NotAvailable)
            return;

        state.Progress = BlackJackGameProgress.Starting;
        state.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");

        var buffer = ArrayPool<byte>.Create(DefaultBufferSize, 1024);
        
        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession = new ClientWebSocket();
        socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var url = new Uri(config.SocketPort != 0 ? $"ws://{config.SocketEndpoint}:{config.SocketPort}" : $"wss://{config.SocketEndpoint}");
        await socketSession.ConnectAsync(url, cancellationTokenSource.Token);
            
        logger.LogDebug("Successfully connected to WebSocket server");
            
        while (true)
            if (socketSession.State == WebSocketState.Open)
                break;
        
        logger.LogDebug("WebSocket session for BlackJack successfully established");

        while (!state.Progress.Equals(BlackJackGameProgress.Closed))
        {
            var channelTask = RunChannelTask(socketSession,
                state,
                logger,
                cancellationTokenSource);

            var broadcastTask = RunBroadcastTask(socketSession,
                state,
                gameStates,
                guild,
                buffer,
                localizations,
                logger,
                cancellationTokenSource);
            
            await (await Task.WhenAny(channelTask, broadcastTask));
        }
        
        logger.LogDebug("Cleaning up resource...");
        await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "Game is concluded",
            cancellationTokenSource.Token);
        logger.LogDebug("Socket session closed");
        await Task.Delay(TimeSpan.FromSeconds(20));
        var retrieveResult = gameStates.BlackJackGameStates.Item1.TryGetValue(state.GameId, out var gameState);
        if (!retrieveResult)
            return;
        
        foreach (var (_, value) in gameState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.Id;
            await value.TextChannel.DeleteAsync();
            gameStates.BlackJackGameStates.Item2.Remove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        gameStates.BlackJackGameStates.Item1.Remove(state.GameId, out _);
        socketSession.Dispose();
        cancellationTokenSource.Dispose();
    }
    
    public static async Task AddBlackJackPlayerState(string gameId, BlackJackPlayerState playerState, GameStates gameStates)
    {
        var state = gameStates.BlackJackGameStates.Item1.GetOrAdd(gameId, new BlackJackGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
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
                .ToImmutableList().IsEmpty)
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

        if (state.CurrentPlayerOrder != playerState.Order)
            return;

        var message = e.Message;
        var lowercaseContent = message.Content.ToLowerInvariant().Trim();
        var splitContent = lowercaseContent.Split(' ');

        switch (state.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                await SendGenericData(state, playerState, splitContent switch
                {
                    var x when x[0] == "draw" => BlackJackInGameRequestType.Draw,
                    var x when x[0] == "stand" => BlackJackInGameRequestType.Stand
                });
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
                            client.Logger.LogError("An exception occurred when handling message: {Exception}", ex.Message);
                            await e.Message.Channel.SendMessageAsync(
                                localizations.GetLocalization().BlackJack.RaiseInvalidNumber);
                            return;
                        }
                    }
                }

                break;
            }
        }
    }

    private static async Task RunChannelTask(
        WebSocket socketSession,
        BlackJackGameState state,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        // Receive serialized data from channel without blocking.
        var channelMessage = await state.Channel!.Reader.ReadAsync();
        var (playerId, payload) = channelMessage;
        var operation = await HandleChannelMessage(playerId, payload, socketSession,
            state, logger, cancellationTokenSource.Token);

        if (operation.Equals(SocketOperation.Continue)) 
            return;

        var message = operation switch
        {
            SocketOperation.Shutdown => "WebSocket session ends due to timeout",
            SocketOperation.Close => "Received close response",
            _ => ""
        };
        logger.LogDebug("{Message}", message);
    }

    private static async Task RunBroadcastTask(
        WebSocket socketSession,
        BlackJackGameState state,
        GameStates gameStates,
        DiscordGuild guild,
        ArrayPool<byte> buffer,
        Localizations localizations,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        // Try receiving broadcast messages from WS server without blocking.
        var rentBuffer = buffer.Rent(DefaultBufferSize);
        var receiveResult = await socketSession.ReceiveAsync(rentBuffer, cancellationTokenSource.Token);

        logger.LogDebug("Received broadcast message from WS");
        var receiveContent = Encoding.UTF8.GetString(rentBuffer[..receiveResult.Count]);
        buffer.Return(rentBuffer, true);
        if (!await HandleProgress(receiveContent, state, gameStates, localizations, guild, logger))
            await HandleEndingResult(receiveContent, state, localizations, logger);
    }

    private static async Task<SocketOperation> HandleChannelMessage(ulong playerId,
        byte[] payload,
        WebSocket socketSession,
        BlackJackGameState state,
        ILogger logger,
        CancellationToken token)
    {
        logger.LogDebug("Received message from channel");
        var payloadText = Encoding.UTF8.GetString(payload);
        if (playerId == 0 && payloadText.Equals(SocketOperation.Shutdown.ToString()))
        {
            state.Progress = BlackJackGameProgress.Closed;
            logger.LogDebug("Closing the session due to timeout");
            return SocketOperation.Shutdown;
        }

        var requestTypeString = InGameRequestTypes
            .Where(s => payloadText.Contains(s))
            .First(s => Enum.TryParse<BlackJackInGameRequestType>(s, out var _));
        var _ = Enum.TryParse<BlackJackInGameRequestType>(requestTypeString, out var requestType);

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(payload), WebSocketMessageType.Text,
            true, token);

        if (requestType.Equals(BlackJackInGameRequestType.Close))
        {
            state.Progress = BlackJackGameProgress.Closed;
            logger.LogDebug("Received close response. Closing the session...");
            return SocketOperation.Close;
        }

        /*var buffer = _channelBuffer.Rent(DefaultBufferSize);
        var socketResponse = await socketSession.ReceiveAsync(buffer.AsMemory(), token);

        try
        {
            var bufferText = Encoding.UTF8.GetString(buffer[..socketResponse.Count]);
            _channelBuffer.Return(buffer, true);
            var deserializedStateData = JsonSerializer.Deserialize<RawBlackJackGameState>(bufferText);
            var previousHighestBet = state.HighestBet;
            var __ = UpdateGameState(state, deserializedStateData, guild);

            var result = state.Players.TryGetValue(playerId, out var playerState);
            if (!result)
                return SocketOperation.Continue;

            await SendProgressMessages(state, playerState, requestType, previousHighestBet, deserializedStateData,
                localizations, logger);
        }
        catch (Exception ex)
        {
            logger.LogError("An error occurred when deserializing received response from WebSocket: {Exception}", ex.Message);
        }*/

        return SocketOperation.Continue;
    }

    private static async Task<bool> HandleProgress(
        string messageContent,
        BlackJackGameState state,
        GameStates gameStates,
        Localizations localizations,
        DiscordGuild guild,
        ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawBlackJackGameState>(messageContent, JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired");

            if (!state.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData, state, gameStates, localizations);
                Semaphore.Release();
                logger.LogDebug("Semaphore released");
                return progressChangeResult;
            }
            
            var previousHighestBet = state.HighestBet;
            await UpdateGameState(state, deserializedIncomingData, guild);
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result = state.Players.TryGetValue(playerId, out var playerState);
            if (!result)
                return false;

            result = Enum.TryParse<BlackJackInGameRequestType>(deserializedIncomingData.PreviousRequestType,
                out var requestType);
            if (!result)
                return false;

            await SendProgressMessages(state, playerState, requestType, previousHighestBet,
                deserializedIncomingData, localizations, logger);

            Semaphore.Release();
            logger.LogDebug("Semaphore released");
        }
        catch (JsonSerializationException _)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("An exception occurred when handling progress: {Exception}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
            Semaphore.Release();
            logger.LogDebug("Semaphore released");
            return false;
        }

        return true;
    }

    private static async Task<bool> HandleProgressChange(RawBlackJackGameState deserializedIncomingData, BlackJackGameState state,
        GameStates gameStates, Localizations localizations)
    {
        if (deserializedIncomingData.Progress.Equals(BlackJackGameProgress.Ending))
            return true;

        state.Progress = deserializedIncomingData.Progress;
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
                        localizations
                    );
                    await playerState.Value.TextChannel.SendMessageAsync(embed);
                }
                break;
            }
        }

        return true;
    }

    private static async Task HandleEndingResult(string messageContent, BlackJackGameState state, Localizations localizations, ILogger logger)
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
                .WithColor((int)BurstColor.Kotlin)
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
                        .Replace("{totalPoints}", totalPoints.ToString()));
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
            logger.LogError("An exception occurred when handling ending result: {Exception}", ex.Message);
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
        Console.WriteLine("Received raise action.");
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
        BlackJackPlayerState? playerState,
        BlackJackInGameRequestType requestType,
        int previousHighestBet,
        RawBlackJackGameState? deserializedStateData,
        Localizations localizations,
        ILogger logger
    )
    {
        if (deserializedStateData == null)
            return;
        
        switch (state.Progress)
        {
            case BlackJackGameProgress.Starting:
                await SendInitialMessage(playerState, localizations, logger);
                break;
            case BlackJackGameProgress.Progressing:
                await SendDrawingMessage(
                    state,
                    playerState,
                    state.CurrentPlayerOrder,
                    requestType,
                    deserializedStateData.Progress,
                    localizations
                );
                break;
            case BlackJackGameProgress.Gambling:
                await SendGamblingMessage(
                    state, playerState, state.CurrentPlayerOrder, requestType, previousHighestBet,
                    deserializedStateData.Progress, localizations);
                break;
        }
    }

    private static async Task SendInitialMessage(BlackJackPlayerState? playerState, Localizations localizations, ILogger logger)
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

        await playerState.TextChannel.SendMessageAsync(new DiscordEmbedBuilder()
            .WithAuthor(playerState.PlayerName, iconUrl: playerState.AvatarUrl)
            .WithColor((int)BurstColor.Kotlin)
            .WithTitle(localization.InitialMessageTitle)
            .WithDescription(description)
            .WithFooter(localization.InitialMessageFooter)
            .WithThumbnail(Constants.BurstLogo));
    }

    private static async Task SendDrawingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? playerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType requestType,
        BlackJackGameProgress nextProgress,
        Localizations localizations
    )
    {
        if (playerState == null || playerState.TextChannel == null)
            return;

        var previousPlayerOrder = playerState.Order;
        var lastCard = playerState.Cards.Last();
        var currentPoints = playerState.Cards.GetRealizedValues(100);
        var localization = localizations.GetLocalization();
        
        foreach (var (_, state) in gameState.Players)
        {
            if (state.TextChannel == null)
                continue;
            
            var isPreviousPlayer = previousPlayerOrder == state.Order;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : playerState.PlayerName;

            var authorText = BuildPlayerActionMessage(localizations, requestType, pronoun, lastCard);

            var embed = new DiscordEmbedBuilder()
                .WithAuthor(authorText, iconUrl: playerState.AvatarUrl)
                .WithColor((int)BurstColor.Kotlin);

            if (isPreviousPlayer)
            {
                embed = embed
                    .WithDescription(
                        localization.BlackJack.CardPoints.Replace("{cardPoints}", currentPoints.ToString()));
            }

            await state.TextChannel.SendMessageAsync(embed);
        }

        if (!gameState.Progress.Equals(nextProgress)) 
            return;

        var currentPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == currentPlayerOrder)
            .Value;
        foreach (var state in gameState.Players)
        {
            if (state.Value.TextChannel == null)
                continue;
            
            var embed = BuildTurnMessage(state, currentPlayerOrder, currentPlayer, gameState, localizations);
            await state.Value.TextChannel.SendMessageAsync(embed);
        }
    }

    private static async Task SendGamblingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? playerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType requestType,
        int previousHighestBet,
        BlackJackGameProgress nextProgress,
        Localizations localizations
    )
    {
        if (playerState == null || playerState.TextChannel == null)
            return;

        var previousPlayerOrder = playerState.Order;
        var currentPoints = playerState.Cards.GetRealizedValues(100);
        var localization = localizations.GetLocalization();

        foreach (var (_, state) in gameState.Players)
        {
            if (state.TextChannel == null)
                continue;

            var isPreviousPlayer = previousPlayerOrder == state.Order;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : playerState.PlayerName;

            var verb = isPreviousPlayer
                ? localization.GenericWords.ParticipateSecond
                : localization.GenericWords.ParticipateThird;

            var authorText = BuildPlayerActionMessage(
                localizations, requestType, pronoun,
                highestBet: gameState.HighestBet,
                verb: verb,
                diff: gameState.HighestBet - previousHighestBet);

            var embed = new DiscordEmbedBuilder()
                .WithAuthor(authorText, null, playerState.AvatarUrl)
                .WithColor((int)BurstColor.Kotlin);

            if (isPreviousPlayer)
            {
                embed = embed.WithDescription(
                    localization.BlackJack.CardPoints.Replace(
                        "{cardPoints}",
                        currentPoints.ToString()
                    )
                );
            }

            await state.TextChannel.SendMessageAsync(embed);
        }

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

            var embed = BuildTurnMessage(state, currentPlayerOrder, currentPlayer, gameState, localizations);
            await state.Value.TextChannel.SendMessageAsync(embed);
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
                .Replace("{lastCard}", lastCard!.ToString()),
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

    private static DiscordEmbedBuilder BuildTurnMessage(
        KeyValuePair<ulong, BlackJackPlayerState> entry,
        int currentPlayerOrder,
        BlackJackPlayerState currentPlayer,
        BlackJackGameState gameState,
        Localizations localizations)
    {
        var localization = localizations.GetLocalization();
        var state = entry.Value;
        var isCurrentPlayer = state.Order == currentPlayerOrder;
        
        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond
            : localization.GenericWords.PossessiveThird
                .Replace("{playerName}", currentPlayer.PlayerName);
        
        var cardNames = $"{possessive} cards:\n" + string.Join('\n', currentPlayer.Cards
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
            .WithColor((int)BurstColor.Kotlin)
            .WithTitle(title);

        switch (gameState.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                if (isCurrentPlayer)
                {
                    embed = embed.WithFooter(localization.BlackJack.ProgressingFooter);
                }

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

        return embed;
    }

    private static async Task UpdateGameState(BlackJackGameState state, RawBlackJackGameState? data, DiscordGuild guild)
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
        {
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
                var textChannel = (await guild
                        .GetChannelsAsync())
                    .First(c => c.Id == playerState.ChannelId);
                player.TextChannel = textChannel;
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
}