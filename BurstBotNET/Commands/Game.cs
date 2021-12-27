using System.Net.WebSockets;
using BurstBotShared.Shared.Models.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BurstBotNET.Commands;

#pragma warning disable CA2252
public static class Game
{
    public static readonly JsonSerializerSettings JsonSerializerSettings = new();

    static Game()
    {
        JsonSerializerSettings.MissingMemberHandling = MissingMemberHandling.Error;
    }
    
    public static async Task GenericCloseGame(WebSocket socketSession, ILogger logger, CancellationTokenSource cancellationTokenSource)
    {
        logger.LogDebug("Cleaning up resource...");
        await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "Game is concluded",
            cancellationTokenSource.Token);
        logger.LogDebug("Socket session closed");
        _ = Task.Run(() =>
        {
            cancellationTokenSource.Cancel();
            logger.LogDebug("All tasks cancelled");
            cancellationTokenSource.Dispose();
        });
        await Task.Delay(TimeSpan.FromSeconds(60));
    }

    public static async Task<WebSocket> GenericOpenWebSocketSession(string gameName, Config config, ILogger logger, CancellationTokenSource cancellationTokenSource)
    {
        var socketSession = new ClientWebSocket();
        socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var url = new Uri(config.SocketPort != 0
            ? $"ws://{config.SocketEndpoint}:{config.SocketPort}"
            : $"wss://{config.SocketEndpoint}");
        await socketSession.ConnectAsync(url, cancellationTokenSource.Token);
        
        logger.LogDebug("Successfully connected to WebSocket server");

        while (true)
            if (socketSession.State == WebSocketState.Open)
                break;

        logger.LogDebug("WebSocket session for {GameName} successfully established", gameName);
        return socketSession;
    }
}