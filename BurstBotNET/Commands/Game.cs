using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Api;
using BurstBotShared.Shared;
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

    /// <summary>
    /// Build a player list including the invoker of the slash command, validate if all players have enough tips, send a REST call to the server to request a game match, and return the join result or error messages when failed.
    /// </summary>
    /// <param name="baseBet">The base bet that is required for each player to join the game.</param>
    /// <param name="users">Invited players mentioned by using additional options in the slash command.</param>
    /// <param name="gameType">The game type of which to request a game match.</param>
    /// <param name="joinRequestEndpoint">The REST endpoint for requesting game matches. All games have dedicated REST endpoints.</param>
    /// <param name="state">The container for all relevant states.</param>
    /// <param name="context">The interaction context generated after a user invokes a slash command.</param>
    /// <param name="interactionApi">REST API for handling Discord interactions.</param>
    /// <param name="userApi">REST API for handling Discord users.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A join result on success, or null when failed.</returns>
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

    /// <summary>
    /// Build an embed and collect players' reactions/confirmations. Used when a player directly invited all players he wants to play a game with.
    /// </summary>
    /// <param name="startGameData">A generic container for all relevant information required to collect reactions and act accordingly.</param>
    /// <returns>An immutable array with all players casted to guild members, and a match data with a game ID; or null when failed (e.g. the player invites other players but not all invited players confirm to join the game, resulting a failed match).</returns>
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

    /// <summary>
    /// Validate if all participants have enough tips to join a game.
    /// </summary>
    /// <param name="invokerId">The user who invokes the slash command.</param>
    /// <param name="playerIds">Player IDs whose tips will be validated.</param>
    /// <param name="burstApi">A generic API service for handling HTTP requests and responses.</param>
    /// <param name="context">The interaction context generated after a user invokes the slash command.</param>
    /// <param name="minimumRequiredTip">Minimum amount of tips to join the game.</param>
    /// <param name="interactionApi">REST API for handling Discord interactions.</param>
    /// <returns>Whether all players pass the validation, and optionally the invoking player's tips.</returns>
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