using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;
using Remora.Results;

namespace BurstBotNET.Commands.OldMaid;

using OldMaidGame = IGame<OldMaidGameState, RawOldMaidGameState, OldMaid, OldMaidPlayerState, OldMaidGameProgress, OldMaidInGameRequestType>;

public partial class OldMaid
{
    private async Task<IResult> Join(float baseBet, params IUser?[] users)
    {
        var joinResult = await Game.GenericJoinGame(
            baseBet, users, GameType.OldMaid, "/old_maid/join",
            _state, _context, _interactionApi, _userApi, _logger);

        if (joinResult == null) return Result.FromSuccess();

        switch (joinResult.JoinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var startGameData = new GenericStartGameData
                {
                    BotUser = joinResult.BotUser,
                    ChannelApi = _channelApi,
                    ConfirmationEndpoint = "/old_maid/join/confirm",
                    Context = _context,
                    GameName = GameName,
                    GuildApi = _guildApi,
                    InteractionApi = _interactionApi,
                    InvokingMember = joinResult.InvokingMember,
                    JoinStatus = joinResult.JoinStatus,
                    Logger = _logger,
                    State = _state,
                    PlayerIds = joinResult.MentionedPlayers.Select(s => s.Value),
                    MinPlayerCount = 4,
                    Reply = joinResult.Reply
                };
                
                var result = await Game.GenericStartGame(startGameData);
                
                if (!result.HasValue) return Result.FromSuccess();

                var (members, matchData) = result.Value;
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                
                foreach (var member in members)
                {
                    var textChannel =
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, joinResult.BotUser,
                            member, _guildApi, _logger);

                    var playerState = new OldMaidPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel
                    };

                    await AddPlayerStateAndStartListening(matchData, playerState, guild.Value);
                }
                
                break;
            }
            case GenericJoinStatusType.Matched:
            {
                var matchedMessageResult = await _interactionApi
                    .EditOriginalInteractionResponseAsync(
                        _context.ApplicationID,
                        _context.Token,
                        joinResult.Reply);
                if (!matchedMessageResult.IsSuccess) return Result.FromError(matchedMessageResult);

                var followUpResult = await _interactionApi
                    .CreateFollowupMessageAsync(
                        _context.ApplicationID,
                        _context.Token,
                        embeds: new[]
                        {
                            Utilities.BuildGameEmbed(joinResult.InvokingMember, joinResult.BotUser,
                                joinResult.JoinStatus, GameName, "",
                                null)
                        });

                if (!followUpResult.IsSuccess) return Result.FromError(followUpResult);
                
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();

                var textChannel = await _state.BurstApi.CreatePlayerChannel(
                    guild.Value,
                    joinResult.BotUser,
                    joinResult.InvokingMember,
                    _guildApi,
                    _logger);

                var playerState = new OldMaidPlayerState
                {
                    AvatarUrl = joinResult.InvokingMember.GetAvatarUrl(),
                    GameId = joinResult.JoinStatus.GameId ?? "",
                    PlayerId = joinResult.InvokingMember.User.Value.ID.Value,
                    PlayerName = joinResult.InvokingMember.GetDisplayName(),
                    TextChannel = textChannel
                };

                await AddPlayerStateAndStartListening(joinResult.JoinStatus, playerState, guild.Value);
                
                break;
            }
            case GenericJoinStatusType.Waiting:
            {
                var waitingMessageResult = await _interactionApi
                    .EditOriginalInteractionResponseAsync(
                        _context.ApplicationID,
                        _context.Token,
                        joinResult.Reply);
                if (!waitingMessageResult.IsSuccess) return Result.FromError(waitingMessageResult);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await _state.BurstApi.WaitForGame<OldMaidPlayerState>(
                            joinResult.JoinStatus, _context, joinResult.MentionedPlayers,
                            joinResult.BotUser, "", GameName, _state.AmqpService, _interactionApi, _guildApi, _logger);
                        if (!waitingResult.HasValue) return;

                        var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                        if (!guild.HasValue) return;

                        var (matchData, playerStates) = waitingResult.Value;
                        foreach (var player in playerStates)
                            await AddPlayerStateAndStartListening(matchData, player, guild.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("WebSocket failed: {Exception}", ex);
                    }
                });
                break;
            }
        }
        
        return Result.FromSuccess();
    }

    public async Task AddPlayerStateAndStartListening(GenericJoinStatus? joinStatus, OldMaidPlayerState playerState, Snowflake guild)
    {
        await OldMaidGame.AddPlayerState(joinStatus?.GameId ?? "", guild, playerState, new OldMaidInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel!.ID.Value,
                ClientType = ClientType.Discord,
                GameId = joinStatus?.GameId ?? "",
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName,
                RequestType = OldMaidInGameRequestType.Deal
            },
            _state.GameStates.OldMaidGameStates.Item1,
            _state.GameStates.OldMaidGameStates.Item2);
                            
        await Task.Delay(TimeSpan.FromSeconds(1));
                            
        _ = Task.Run(() => OldMaidGame.StartListening(joinStatus?.GameId ?? "",
            _state.GameStates.OldMaidGameStates,
            GameName,
            OldMaidGameProgress.NotAvailable,
            OldMaidGameProgress.Starting,
            OldMaidGameProgress.Closed,
            OldMaidGame.InGameRequestTypes,
            OldMaidInGameRequestType.Close,
            Game.GenericOpenWebSocketSession,
            Game.GenericCloseGame,
            _state,
            _channelApi,
            _guildApi,
            _logger));
    }
}