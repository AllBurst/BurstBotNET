using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using BurstBotShared.Services;
using BurstBotShared.Shared.Enums;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Interfaces;

public interface IGame<in TState, in TRaw, TGame, in TPlayerState, TProgress, TInGameRequestType>
    where TState: IState<TState, TRaw, TProgress>, IGameState<TPlayerState, TProgress>
    where TRaw: IRawState<TState, TRaw, TProgress>
    where TGame: IGame<TState, TRaw, TGame, TPlayerState, TProgress, TInGameRequestType>
    where TProgress: Enum
    where TInGameRequestType: struct, Enum
    where TPlayerState: IPlayerState
{
#pragma warning disable CA2252
    static abstract Task AddPlayerState(string gameId,
        Snowflake guild,
        TPlayerState playerState,
        GameStates gameStates);
    
    static abstract Task StartListening(
        string gameId,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger);
    
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
    
    static async Task RunChannelTask(WebSocket socketSession,
        TState state,
        IEnumerable<string> inGameRequestTypes,
        TInGameRequestType closeRequestType,
        TProgress closedProgress,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        var channelMessage = await state.Channel!.Reader.ReadAsync();
        var (playerId, payload) = channelMessage;
        var operation =
            await HandleChannelMessage(playerId, payload, socketSession, state, inGameRequestTypes, closeRequestType, closedProgress, logger, cancellationTokenSource.Token);

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

    static async Task RunBroadcastTask(
        WebSocket socketSession,
        TState gameState,
        ArrayPool<byte> buffer,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        var contentStack = new Stack<string>();
        WebSocketReceiveResult? receiveResult;

        do
        {
            var rentBuffer = buffer.Rent(Constants.BufferSize);
            receiveResult = await socketSession.ReceiveAsync(rentBuffer, cancellationTokenSource.Token);
            logger.LogDebug("Received broadcast message from WS");
            var receiveContent = rentBuffer[..receiveResult.Count];
            var stringContent = Encoding.UTF8.GetString(receiveContent);
            contentStack.Push(stringContent);
            //logger.LogDebug("Received content: {Content}", stringContent);
            buffer.Return(rentBuffer, true);
        } while (!receiveResult.EndOfMessage);

        var content = new StringBuilder();
        while (contentStack.TryPop(out var str))
            content.Append(str);

        if (!await TGame.HandleProgress(content.ToString(), gameState, state, channelApi, guildApi, logger))
            await TGame.HandleEndingResult(content.ToString(), gameState, state.Localizations, state.DeckService,
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
        catch (TaskCanceledException ex)
        {
            logger.LogDebug("Timeout task has been cancelled: {@Exception}", ex);
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogDebug("Cancellation source has been disposed (timeout task): {@Exception}", ex);
        }
    }

    static async Task<SocketOperation> HandleChannelMessage(ulong playerId, byte[] payload, WebSocket socketSession,
        TState state,
        IEnumerable<string> inGameRequestTypes,
        TInGameRequestType closeRequestType,
        TProgress closedProgress, ILogger logger, CancellationToken token)
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
            .FirstOrDefault(s => Enum.TryParse<TInGameRequestType>(s, out var _));

        if (string.IsNullOrWhiteSpace(requestTypeString))
            return SocketOperation.Continue;

        var parseResult = Enum.TryParse<TInGameRequestType>(requestTypeString, out var requestType);

        if (!parseResult)
            return SocketOperation.Continue;

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(payload), WebSocketMessageType.Text,
            true, token);

        if (!requestType.Equals(closeRequestType)) return SocketOperation.Continue;

        state.Progress = closedProgress;
        logger.LogDebug("Received close response. Closing the session...");
        return SocketOperation.Close;
    }
}