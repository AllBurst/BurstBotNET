using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BurstBotShared.Services;
using BurstBotShared.Shared.Enums;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Interfaces;

public interface IGame<in TState, in TRaw, TGame, in TPlayerState, TProgress, TInGameRequestType>
    where TState: IState<TState, TRaw, TProgress>, IGameState<TPlayerState, TProgress>, IDisposable, new()
    where TRaw: IRawState<TState, TRaw, TProgress>
    where TGame: IGame<TState, TRaw, TGame, TPlayerState, TProgress, TInGameRequestType>
    where TProgress: Enum
    where TInGameRequestType: struct, Enum
    where TPlayerState: IPlayerState
{
#pragma warning disable CA2252
    static abstract Task<bool> HandleProgress(
        string messageContent,
        TState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger);
    
    static abstract Task<bool> HandleProgressChange(
        TRaw deserializedIncomingData,
        TState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger);

    static abstract Task HandleEndingResult(
        string messageContent,
        TState state,
        Localizations localizations,
        DeckService deckService,
        IDiscordRestChannelAPI channelApi,
        ILogger logger);
#pragma warning restore CA2252

    static ImmutableArray<string> InGameRequestTypes => Enum.GetNames<TInGameRequestType>().ToImmutableArray();

    static async Task AddPlayerState<TRequest>(
        string gameId,
        Snowflake guild,
        TPlayerState playerState,
        TRequest dealRequest,
        ConcurrentDictionary<string, TState> gameStates,
        ConcurrentHashSet<Snowflake> textChannels,
        AmqpService amqpService) where TRequest: IGenericDealData
    {
        var state = gameStates
            .GetOrAdd(gameId, new TState());
        state.Players.AddOrUpdate(playerState.PlayerId, playerState, (_, pState) =>
        {
            pState.AvatarUrl = pState.AvatarUrl == string.Empty ? playerState.AvatarUrl : pState.AvatarUrl;
            pState.PlayerName = pState.PlayerName == string.Empty ? playerState.PlayerName : pState.PlayerName;
            pState.TextChannel ??= playerState.TextChannel;
            return pState;
        });
        state.Guilds.Add(guild);
        state.RequestChannel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();
        state.ResponseChannel ??= Channel.CreateUnbounded<byte[]>();

        amqpService.RegisterResponseChannel(gameId, state.ResponseChannel);

        if (playerState.TextChannel == null) return;

        textChannels.Add(playerState.TextChannel.ID);
        await state.RequestChannel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(dealRequest)));
    }

    static async Task StartListening(
        string gameId,
        Tuple<ConcurrentDictionary<string, TState>, ConcurrentHashSet<Snowflake>> gameStateTuple,
        string gameName,
        TProgress notAvailableProgress,
        TProgress startingProgress,
        TProgress closedProgress,
        ImmutableArray<string> inGameRequestTypes,
        TInGameRequestType closeRequestType,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var gameState = gameStateTuple.Item1
            .GetOrAdd(gameId, new TState());
        logger.LogDebug("{GameName} game progress: {Progress}", gameName, gameState.Progress);
        
        await gameState.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        
        if (!gameState.Progress.Equals(notAvailableProgress))
        {
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }

        gameState.Progress = startingProgress;
        gameState.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");
        
        var cancellationTokenSource = new CancellationTokenSource();
        gameState.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");
        
        var timeout = state.Config.Timeout;
        while (!gameState.Progress.Equals(closedProgress))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = RunChannelTask(gameState, inGameRequestTypes,
                closeRequestType, closedProgress, state.AmqpService, logger, cancellationTokenSource);

            var broadcastTask = RunBroadcastTask(gameState, state,
                channelApi,
                guildApi,
                logger, cancellationTokenSource);

            var timeoutTask = RunTimeoutTask(timeout, gameState, closedProgress,
                logger,
                timeoutCancellationTokenSource);

            await await Task.WhenAny(channelTask, broadcastTask, timeoutTask);
            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
                timeoutCancellationTokenSource.Dispose();
            }, default);
        }

        _ = Task.Run(() =>
        {
            cancellationTokenSource.Cancel();
            logger.LogDebug("All tasks cancelled");
            cancellationTokenSource.Dispose();
        });
        
        await Task.Delay(TimeSpan.FromSeconds(60), default);
        
        var retrieveResult = gameStateTuple.Item1
            .TryGetValue(gameState.GameId, out var retrievedState);
        if (!retrieveResult) return;

        foreach (var (_, value) in retrievedState!.Players)
        {
            if (value.TextChannel == null) continue;

            var channelId = value.TextChannel.ID;

            var deleteResult = await channelApi
                .DeleteChannelAsync(channelId);
            if (!deleteResult.IsSuccess)
                logger.LogError("Failed to delete player's channel: {Reason}, inner: {Inner}",
                    deleteResult.Error.Message, deleteResult.Inner);

            gameStateTuple.Item2.TryRemove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1), default);
        }
        
        state.AmqpService.UnregisterResponseChannel(gameState.GameId);
        gameState.Dispose();
        gameStateTuple.Item1.Remove(gameState.GameId, out _);
    }

    static async Task RunChannelTask(
        TState state,
        IEnumerable<string> inGameRequestTypes,
        TInGameRequestType closeRequestType,
        TProgress closedProgress,
        AmqpService amqpService,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        var channelMessage = await state.RequestChannel!.Reader.ReadAsync(cancellationTokenSource.Token);
        var (playerId, payload) = channelMessage;
        var operation =
            await HandleChannelMessage(playerId, payload, state, inGameRequestTypes, closeRequestType, closedProgress,
                amqpService, logger, cancellationTokenSource.Token);

        if (operation.Equals(SocketOperation.Continue))
            return;
        
        var message = operation switch
        {
            SocketOperation.Shutdown => "Game session ends due to timeout",
            SocketOperation.Close => "Received close response",
            _ => ""
        };
        logger.LogDebug("{Message}", message);
    }

    static async Task RunBroadcastTask(
        TState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        var payload = await gameState.ResponseChannel!.Reader.ReadAsync(cancellationTokenSource.Token);
        var content = Encoding.UTF8.GetString(payload);

        if (!await TGame.HandleProgress(content, gameState, state, channelApi, guildApi, logger))
            await TGame.HandleEndingResult(content, gameState, state.Localizations, state.DeckService,
                channelApi, logger);
    }

    static async Task RunTimeoutTask(long timeout, TState state, TProgress closedProgress, ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancellationTokenSource.Token);
            logger.LogDebug("Game timed out due to inactivity");
            state.Progress = closedProgress;
        }
        catch (TaskCanceledException)
        {
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogDebug("Cancellation source has been disposed (timeout task): {@Exception}", ex);
        }
    }

    static async Task<SocketOperation> HandleChannelMessage(
        ulong playerId,
        byte[] payload,
        TState state,
        IEnumerable<string> inGameRequestTypes,
        TInGameRequestType closeRequestType,
        TProgress closedProgress,
        AmqpService amqpService,
        ILogger logger,
        CancellationToken token)
    {
        logger.LogDebug("Received message from channel");
        var payloadText = Encoding.UTF8.GetString(payload);
        if (playerId == 0 && payloadText.Equals(SocketOperation.Shutdown.ToString()))
        {
            state.Progress = closedProgress;
            logger.LogDebug("Closing the session due to timeout");
            return SocketOperation.Shutdown;
        }
        
        var requestTypeString = inGameRequestTypes
            .Where(s => payloadText.Contains(s))
            .FirstOrDefault(s => Enum.TryParse<TInGameRequestType>(s, out _));

        if (string.IsNullOrWhiteSpace(requestTypeString)) return SocketOperation.Continue;

        var parseResult = Enum.TryParse<TInGameRequestType>(requestTypeString, out var requestType);

        if (!parseResult) return SocketOperation.Continue;

        await amqpService.SendInGameRequestData(payload, state.GameType, state.GameId);

        if (!requestType.Equals(closeRequestType)) return SocketOperation.Continue;

        state.Progress = closedProgress;
        logger.LogDebug("Received close response. Closing the session...");
        return SocketOperation.Close;
    }

    Task AddPlayerStateAndStartListening(
        GenericJoinStatus? joinStatus,
        TPlayerState playerState,
        Snowflake guild
    );
}