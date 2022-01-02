using System.Collections.Immutable;
using System.Net.WebSockets;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

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

    public static async Task<(GenericJoinStatus, string)?> GenericJoinGame(
        List<ulong> playerIds,
        GameType gameType,
        string requestEndpoint,
        BurstApi burstApi,
        InteractionContext context,
        IDiscordRestInteractionAPI interactionApi
    )
    {
        var joinRequest = new GenericJoinRequest
        {
            ClientType = ClientType.Discord,
            GameType = gameType,
            PlayerIds = playerIds
        };
        var joinResponse = await burstApi.SendRawRequest(requestEndpoint, ApiRequestType.Post, joinRequest);
        var playerCount = playerIds.Count;
        var unit = playerCount > 1 ? "players" : "player";

        var (joinStatus, reply) = BurstApi.HandleMatchGameHttpStatuses(joinResponse, unit, gameType);
        if (joinStatus != null) return (joinStatus, reply);
        var _ = await interactionApi
            .EditOriginalInteractionResponseAsync(
                context.ApplicationID,
                context.Token,
                reply);
        return null;
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

    public static async Task<(ImmutableArray<IGuildMember>, GenericJoinStatus)?> GenericStartGame(
        InteractionContext context,
        string reply,
        IGuildMember invokingMember,
        IUser botUser,
        GenericJoinStatus joinStatus,
        string gameName,
        string confirmationEndpoint,
        IEnumerable<ulong> playerIds,
        State state,
        int minPlayerCount,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        var startResult = await interactionApi
            .EditOriginalInteractionResponseAsync(
                context.ApplicationID,
                context.Token,
                reply,
                new[]
                {
                    Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, gameName,
                        "", null)
                });

        if (!startResult.IsSuccess)
        {
            logger.LogError("Failed to edit message for starting game: {Reason}, inner: {Inner}",
                startResult.Error.Message, startResult.Inner);
            return null;
        }
        
        var reactionResult = await BurstApi
            .HandleStartGameReactions(
                gameName, context, startResult.Entity, invokingMember, botUser, joinStatus,
                playerIds, confirmationEndpoint, state,
                channelApi, interactionApi, guildApi, logger, minPlayerCount);

        return reactionResult;
    }

    public static async Task<(bool, RawTip?)> ValidatePlayers(
        ulong invokerId,
        IEnumerable<ulong> playerIds,
        BurstApi burstApi,
        InteractionContext context,
        float minimumRequiredTip,
        IDiscordRestInteractionAPI interactionApi)
    {
        RawTip? tip = null;
        var getTipTasks = playerIds
            .Select(async p =>
            {
                var response = await burstApi.SendRawRequest<object>($"/tip/{p}", ApiRequestType.Get, null);
                if (!response.ResponseMessage.IsSuccessStatusCode)
                    return null;

                var playerTip = await response.GetJsonAsync<RawTip>();
                if (invokerId == p)
                    tip = playerTip;
                
                return playerTip.Amount < minimumRequiredTip ? null : playerTip;
            });

        var playerTips = await Task.WhenAll(getTipTasks);
        var hasInvalidPlayer = playerTips.Any(t => t == null);

        if (!hasInvalidPlayer) return (true, tip);
        
        var _ = await interactionApi
            .EditOriginalInteractionResponseAsync(
                context.ApplicationID,
                context.Token,
                ErrorMessages.InvalidPlayer);
        return (false, null);
    }
}