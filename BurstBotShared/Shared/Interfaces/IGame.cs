using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using BurstBotShared.Services;
using BurstBotShared.Shared.Enums;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;

namespace BurstBotShared.Shared.Interfaces;

public interface IGame<in TState, TRaw, TGame, TProgress, TInGameRequestType>
    where TState: IState<TState, TRaw, TProgress>
    where TRaw: IRawState<TState, TRaw, TProgress>
    where TGame: IGame<TState, TRaw, TGame, TProgress, TInGameRequestType>
    where TProgress: Enum
    where TInGameRequestType: struct, Enum
{
#pragma warning disable CA2252
    static abstract Task<bool> HandleProgress(
        byte[] messageContent,
        TState state,
        GameStates gameStates,
        DeckService deckService,
        Localizations localizations,
        ILogger logger);

    static abstract Task HandleEndingResult(
        byte[] messageContent,
        TState state,
        Localizations localizations,
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
        var channelMessage = await state.PayloadChannel!.Reader.ReadAsync();
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
        TState state,
        GameStates gameStates,
        ArrayPool<byte> buffer,
        DeckService deckService,
        Localizations localizations,
        ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        var rentBuffer = buffer.Rent(Constants.BufferSize);
        var receiveResult = await socketSession.ReceiveAsync(rentBuffer, cancellationTokenSource.Token);
        
        logger.LogDebug("Received broadcast message from WS");
        var receiveContent = rentBuffer[..receiveResult.Count];
        buffer.Return(rentBuffer, true);
        if (!await TGame.HandleProgress(receiveContent, state, gameStates, deckService, localizations, logger))
            await TGame.HandleEndingResult(receiveContent, state, localizations, logger);
    }

    static async Task RunTimeoutTask(long timeout, TState state, TProgress closedProgress, ILogger logger,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(timeout), cancellationTokenSource.Token);
            logger.LogDebug("Game timed out due to inactivity");
            state.GameProgress = closedProgress;
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
            state.GameProgress = closedProgress;
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

        state.GameProgress = closedProgress;
        logger.LogDebug("Received close response. Closing the session...");
        return SocketOperation.Close;
    }
}