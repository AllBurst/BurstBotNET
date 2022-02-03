using System.Collections.Immutable;
using System.Globalization;
using System.Net.WebSockets;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Rest.Core;

namespace BurstBotNET.Commands;

#pragma warning disable CA2252
public static class Game
{
    public static readonly JsonSerializerSettings JsonSerializerSettings = new();
    public static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;

    static Game()
    {
        JsonSerializerSettings.MissingMemberHandling = MissingMemberHandling.Error;
    }

    public static IEnumerable<Snowflake> BuildPlayerList(InteractionContext context, IEnumerable<IUser?> users)
    {
        var mentionedPlayers = new List<Snowflake> { context.User.ID };
        var additionalPlayers = users
            .Where(u => u != null)
            .Select(u => u!.ID);
        mentionedPlayers.AddRange(additionalPlayers);
        return mentionedPlayers;
    }

    public static async Task GenericCloseGame(WebSocket socketSession, ILogger logger, CancellationTokenSource cancellationTokenSource)
    {
        logger.LogDebug("Cleaning up resource...");
        try
        {
            await socketSession.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Game is concluded",
                default);
            logger.LogDebug("Socket session closed");
            _ = Task.Run(() =>
            {
                cancellationTokenSource.Cancel();
                logger.LogDebug("All tasks cancelled");
                cancellationTokenSource.Dispose();
            });
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to close socket session: {Exception}", ex);
        }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }

    public static async Task<GenericJoinResult?> GenericJoinGame(
        float baseBet,
        IEnumerable<IUser?> users,
        GameType gameType,
        string joinRequestEndpoint,
        State state,
        InteractionContext context,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestUserAPI userApi,
        ILogger logger)
    {
        var mentionedPlayers = BuildPlayerList(context, users)
            .ToImmutableArray();

        var playerIds = mentionedPlayers
            .Select(s => s.Value)
            .ToImmutableArray();
        
        var (isValid, invokerTip) = await ValidatePlayers(
            context.User.ID.Value,
            playerIds,
            state.BurstApi,
            context,
            baseBet,
            interactionApi);
        
        if (!isValid) return null;
        
        var joinRequest = new GenericJoinRequest
        {
            ClientType = ClientType.Discord,
            GameType = gameType,
            PlayerIds = playerIds.ToList(),
            BaseBet = baseBet
        };
        var joinResponse = await state.BurstApi.SendRawRequest(joinRequestEndpoint, ApiRequestType.Post, joinRequest);
        var playerCount = playerIds.Length;
        var unit = playerCount > 1 ? "players" : "player";

        var (joinStatus, reply) = BurstApi.HandleMatchGameHttpStatuses(joinResponse, unit, gameType);

        if (joinStatus == null)
        {
            var _ = await interactionApi
                .EditOriginalInteractionResponseAsync(
                    context.ApplicationID,
                    context.Token,
                    reply);
            return null;
        }
        
        var invokingMember = await Utilities.GetUserMember(context, interactionApi,
            ErrorMessages.JoinNotInGuild, logger);
        var botUser = await Utilities.GetBotUser(userApi, logger);

        if (invokingMember == null || botUser == null) return null;

        return new GenericJoinResult
        {
            BotUser = botUser,
            InvokerTip = invokerTip!,
            InvokingMember = invokingMember,
            JoinStatus = joinStatus,
            MentionedPlayers = mentionedPlayers,
            Reply = reply
        };
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
        GenericStartGameData startGameData)
    {
        var startResult = await startGameData.InteractionApi
            .EditOriginalInteractionResponseAsync(
                startGameData.Context.ApplicationID,
                startGameData.Context.Token,
                startGameData.Reply,
                new[]
                {
                    Utilities.BuildGameEmbed(startGameData.InvokingMember, startGameData.BotUser,
                        startGameData.JoinStatus, startGameData.GameName,
                        "", null)
                });

        if (!startResult.IsSuccess)
        {
            startGameData.Logger.LogError("Failed to edit message for starting game: {Reason}, inner: {Inner}",
                startResult.Error.Message, startResult.Inner);
            return null;
        }

        var reactionResult = await BurstApi
            .HandleStartGameReactions(
                startGameData.GameName, startGameData.Context, startResult.Entity, startGameData.InvokingMember,
                startGameData.BotUser, startGameData.JoinStatus,
                startGameData.PlayerIds, startGameData.ConfirmationEndpoint, startGameData.State,
                startGameData.ChannelApi, startGameData.InteractionApi, startGameData.GuildApi, startGameData.Logger,
                startGameData.MinPlayerCount);

        if (reactionResult.HasValue) return reactionResult;
        
        var failureResult = await startGameData.InteractionApi
            .EditOriginalInteractionResponseAsync(
                startGameData.Context.ApplicationID,
                startGameData.Context.Token,
                ErrorMessages.HandleReactionFailed);

        if (!failureResult.IsSuccess)
            startGameData.Logger.LogError(
                "Failed to edit original response for failing to handle reactions: {Reason}, inner: {Inner}",
                failureResult.Error.Message, failureResult.Inner);
        
        return null;
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