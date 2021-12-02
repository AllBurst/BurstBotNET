using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Extensions;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Game.BlackJack;
using BurstBotNET.Shared.Models.Game.BlackJack.Serializables;
using BurstBotNET.Shared.Models.Game.Serializables;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;

namespace BurstBotNET.Commands.BlackJack;

public partial class BlackJack
{
    private enum SocketOperation
    {
        Continue, Shutdown, Close
    }

    private static readonly ImmutableList<string> InGameRequestTypes = Enum
        .GetNames<BlackJackInGameRequestType>()
        .ToImmutableList();

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

        var buffer = new byte[4096];

        while (state.Progress != BlackJackGameProgress.Closed)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var socketSession = new ClientWebSocket();
            socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            var url = new Uri($"wss://{config.SocketEndpoint}:{config.SocketPort}");
            await socketSession.ConnectAsync(url, cancellationTokenSource.Token);
            
            while (true)
            {
                if (socketSession.State == WebSocketState.Open)
                    break;
            }
            
            logger.LogDebug("WebSocket session for BlackJack successfully established");

            while (true)
            {
                // Receive serialized data from channel without blocking.
                var result = state.Channel!.Reader.TryRead(out var channelMessage);
                if (result)
                {
                    var (playerId, payload) = channelMessage!;
                    var operation = await HandleChannelMessage(playerId, payload, socketSession,
                        state, guild,
                        localizations, logger, cancellationTokenSource.Token);
                    if (!operation.Equals(SocketOperation.Continue))
                    {
                        var message = operation switch
                        {
                            SocketOperation.Shutdown => "WebSocket session ends due to timeout",
                            SocketOperation.Close => "Received close response",
                            _ => ""
                        };
                        logger.LogDebug("{Message}", message);
                        await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, message,
                            cancellationTokenSource.Token);
                        break;
                    }
                }
                
                // Try receiving broadcast messages from WS server without blocking.
                var receiveResult = await socketSession.ReceiveAsync(buffer, cancellationTokenSource.Token);
                if (receiveResult.Count <= 0)
                    continue;

                var receiveContent = Encoding.UTF8.GetString(buffer);
                if (!await HandleProgress(receiveContent, state, gameStates, localizations, guild, logger))
                {
                    await HandleEndingResult(receiveContent, state, localizations, logger);
                }
            }
        }
        
        logger.LogDebug("Cleaning up resource...");
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
    }
    
    public static async Task AddBlackJackPlayerState(string gameId, BlackJackPlayerState playerState, GameStates gameStates)
    {
        var state = gameStates.BlackJackGameStates.Item1.GetOrAdd(gameId, new BlackJackGameState());
        state.Players.AddOrUpdate(playerState.PlayerId, playerState, (_, _) => playerState);
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

    private static async Task<SocketOperation> HandleChannelMessage(ulong playerId,
        byte[] payload,
        WebSocket socketSession,
        BlackJackGameState state,
        DiscordGuild guild,
        Localizations localizations,
        ILogger logger,
        CancellationToken token)
    {
        logger.LogDebug("Received message from channel");
        var payloadText = Encoding.UTF8.GetString(payload);
        if (playerId == 0 && payloadText.Equals(SocketOperation.Shutdown.ToString()))
        {
            return SocketOperation.Shutdown;
        }

        var requestTypeString = InGameRequestTypes
            .Where(s => payloadText.Contains(s))
            .First(s => Enum.TryParse<BlackJackInGameRequestType>(s, out var _));
        var _ = Enum.TryParse<BlackJackInGameRequestType>(requestTypeString, out var requestType);

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(payload), WebSocketMessageType.Text,
            WebSocketMessageFlags.None, token);

        if (requestType.Equals(BlackJackInGameRequestType.Close))
        {
            state.Progress = BlackJackGameProgress.Closed;
            logger.LogDebug("Received close response. Closing the session...");
            return SocketOperation.Close;
        }

        var buffer = new byte[4096];
        var incomingResponse = socketSession
            .ReceiveAsync(new Memory<byte>(buffer), token);
        while (true)
        {
            if (incomingResponse.IsCompleted)
                break;
        }

        try
        {
            var deserializedStateData = JsonSerializer.Deserialize<RawBlackJackGameState>(buffer);
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
        }

        return SocketOperation.Continue;
    }

    private async Task<bool> HandleProgress(
        string messageContent,
        BlackJackGameState state,
        GameStates gameStates,
        Localizations localizations,
        DiscordGuild guild,
        ILogger logger)
    {
        try
        {
            var deserializedIncomingData = JsonSerializer.Deserialize<RawBlackJackGameState>(messageContent);
            if (deserializedIncomingData == null)
                return false;

            if (!state.Progress.Equals(deserializedIncomingData.Progress))
            {
                return await HandleProgressChange(deserializedIncomingData, state, gameStates, localizations);
            }

            var previousHighestBet = state.HighestBet;
            UpdateGameState(state, deserializedIncomingData, guild);
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
        }
        catch (Exception ex)
        {
            logger.LogError("An exception occurred when handling progress: {Exception}", ex.Message);
            return false;
        }

        return true;
    }

    private static async Task<bool> HandleProgressChange(RawBlackJackGameState deserializedIncomingData, BlackJackGameState state,
        GameStates gameStates, Localizations localizations)
    {
        if (deserializedIncomingData.Progress.Equals(BlackJackGameProgress.Ending))
            return false;

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
                        .SendMessageAsync(localizations.GetLocalization().BlackJack!
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

            var localization = localizations.GetLocalization().BlackJack!;

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
        }
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

        var localization = localizations.GetLocalization().BlackJack!;
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
            var pronoun = isPreviousPlayer ? localization.GenericWords!.Pronoun : playerState.PlayerName;
            
            var authorText = requestType switch
            {
                BlackJackInGameRequestType.Draw => localization.BlackJack!.Draw
                    .Replace("{playerName}", pronoun)
                    .Replace("{lastCard}", lastCard.ToString()),
                BlackJackInGameRequestType.Stand => localization.BlackJack!.Stand
                    .Replace("{playerName}", pronoun),
                _ => localization.BlackJack!.Unknown
                    .Replace("{playerName}", pronoun)
            };

            var embed = new DiscordEmbedBuilder()
                .WithAuthor(authorText, iconUrl: playerState.AvatarUrl)
                .WithColor((int)BurstColor.Kotlin);

            if (isPreviousPlayer)
            {
                embed = embed
                    .WithDescription(
                        localization.BlackJack!.CardPoints.Replace("{cardPoints}", currentPoints.ToString()));
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
            var pronoun = isPreviousPlayer ? localization.GenericWords!.Pronoun : playerState.PlayerName;

            var verb = isPreviousPlayer
                ? localization.GenericWords!.ParticipateSecond
                : localization.GenericWords!.ParticipateThird;

            var authorText = requestType switch
            {
                BlackJackInGameRequestType.Call => localization.BlackJack!.Call
                    .Replace("{playerName}", pronoun)
                    .Replace("{highestBet}", gameState.HighestBet.ToString()),
                BlackJackInGameRequestType.Fold => localization.BlackJack!.Fold
                    .Replace("{playerName}", pronoun)
                    .Replace("{verb}", verb),
                BlackJackInGameRequestType.Raise => localization.BlackJack!.Raise
                    .Replace("{playerName}", pronoun)
                    .Replace("{diff}", (gameState.HighestBet - previousHighestBet).ToString())
                    .Replace("{highestBet}", gameState.HighestBet.ToString()),
                BlackJackInGameRequestType.AllIn => localization.BlackJack!.Allin
                    .Replace("{playerName}", pronoun)
                    .Replace("{highestBet}", gameState.HighestBet.ToString()),
                _ => localization.BlackJack!.Unknown.Replace("{playerName}", pronoun)
            };

            var embed = new DiscordEmbedBuilder()
                .WithAuthor(authorText, null, playerState.AvatarUrl)
                .WithColor((int)BurstColor.Kotlin);

            if (isPreviousPlayer)
            {
                embed = embed.WithDescription(
                    localization.BlackJack!.CardPoints.Replace(
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
            ? localization.GenericWords!.PossessiveSecond
            : localization.GenericWords!.PossessiveThird
                .Replace("{playerName}", currentPlayer.PlayerName);
        
        var cardNames = $"{possessive} cards:\n" + string.Join('\n', currentPlayer.Cards
            .Where(c => isCurrentPlayer || c.IsFront)
            .Select(c => c.IsFront ? c.ToString() : $"**{c}**"));
        
        var title = localization.BlackJack!.TurnMessageTitle
            .Replace("{possessive}", isCurrentPlayer ? possessive.ToLowerInvariant() : possessive);
        
        var description = cardNames + (isCurrentPlayer
            ? "\n\n" + localization.BlackJack!.CardPoints
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
                    embed = embed.WithFooter(localization.BlackJack!.ProgressingFooter);
                }

                embed = embed.WithDescription(description);
                break;
            }
            case BlackJackGameProgress.Gambling:
            {
                var additionalDescription =
                    description + (isCurrentPlayer ? localization.BlackJack!.TurnMessageDescription : "");
                embed = embed
                    .AddField(localization.BlackJack!.HighestBets, gameState.HighestBet.ToString(), true)
                    .AddField(localization.BlackJack!.YourBets, state.BetTips.ToString(), true)
                    .AddField(localization.BlackJack!.TipsBeforeGame, state.OwnTips.ToString())
                    .WithDescription(additionalDescription);
                break;
            }
        }

        return embed;
    }

    private static BlackJackGameState UpdateGameState(BlackJackGameState state, RawBlackJackGameState? data, DiscordGuild guild)
    {
        if (data == null)
            return state;
        
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
                var textChannel = guild.GetChannel(playerState.ChannelId);
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

        return state;
    }
}