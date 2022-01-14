using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;

namespace BurstBotNET.Commands.ChaseThePig;

using ChasePigGame = IGame<ChasePigGameState, RawChasePigGameState, ChaseThePig, ChasePigPlayerState, ChasePigGameProgress, ChasePigInGameRequestType>;

public partial class ChaseThePig
{
    private async Task<IResult> Join(float baseBet, params IUser?[] users)
    {
        var joinResult = await Game.GenericJoinGame(
            baseBet, users, GameType.ChaseThePig, "/chase_the_pig/join",
            _state, _context, _interactionApi, _userApi, _logger);

        if (joinResult == null) return Result.FromSuccess();

        switch (joinResult.JoinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var result = await Game.GenericStartGame(
                    _context, joinResult.Reply, joinResult.InvokingMember, joinResult.BotUser,
                    joinResult.JoinStatus, GameName, "/chase_the_pig/join/confirm",
                    joinResult.MentionedPlayers.Select(s => s.Value),
                    _state, 4, _interactionApi, _channelApi,
                    _guildApi, _logger);
                
                if (!result.HasValue) return Result.FromSuccess();

                var (members, matchData) = result.Value;
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                
                foreach (var member in members)
                {
                    var textChannel =
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, joinResult.BotUser,
                            member, _guildApi, _logger);

                    var playerState = new ChasePigPlayerState()
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel
                    };

                    await ChasePigGame.AddPlayerState(matchData.GameId ?? "", guild.Value, playerState, new ChasePigInGameRequest
                    {
                        AvatarUrl = playerState.AvatarUrl,
                        ChannelId = playerState.TextChannel!.ID.Value,
                        ClientType = ClientType.Discord,
                        GameId = playerState.GameId,
                        PlayerId = playerState.PlayerId,
                        PlayerName = playerState.PlayerName,
                        RequestType = ChasePigInGameRequestType.Deal
                    }, _state.GameStates.ChasePigGameStates.Item1, _state.GameStates.ChasePigGameStates.Item2);

                    _ = Task.Run(() => ChasePigGame.StartListening(matchData.GameId ?? "",
                        _state.GameStates.ChasePigGameStates,
                        GameName,
                        ChasePigGameProgress.NotAvailable,
                        ChasePigGameProgress.Starting,
                        ChasePigGameProgress.Closed,
                        ChasePigGame.InGameRequestTypes,
                        ChasePigInGameRequestType.Close,
                        Game.GenericOpenWebSocketSession,
                        Game.GenericCloseGame,
                        _state,
                        _channelApi,
                        _guildApi,
                        _logger));
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

                var playerState = new ChasePigPlayerState
                {
                    AvatarUrl = joinResult.InvokingMember.GetAvatarUrl(),
                    GameId = joinResult.JoinStatus.GameId ?? "",
                    PlayerId = joinResult.InvokingMember.User.Value.ID.Value,
                    PlayerName = joinResult.InvokingMember.GetDisplayName(),
                    TextChannel = textChannel
                };

                await ChasePigGame.AddPlayerState(joinResult.JoinStatus.GameId ?? "", guild.Value, playerState,
                    new ChasePigInGameRequest
                    {
                        AvatarUrl = playerState.AvatarUrl,
                        ChannelId = playerState.TextChannel!.ID.Value,
                        ClientType = ClientType.Discord,
                        GameId = playerState.GameId,
                        PlayerId = playerState.PlayerId,
                        PlayerName = playerState.PlayerName,
                        RequestType = ChasePigInGameRequestType.Deal
                    }, _state.GameStates.ChasePigGameStates.Item1, _state.GameStates.ChasePigGameStates.Item2);
                
                _ = Task.Run(() => ChasePigGame.StartListening(joinResult.JoinStatus.GameId ?? "",
                    _state.GameStates.ChasePigGameStates,
                    GameName,
                    ChasePigGameProgress.NotAvailable,
                    ChasePigGameProgress.Starting,
                    ChasePigGameProgress.Closed,
                    ChasePigGame.InGameRequestTypes,
                    ChasePigInGameRequestType.Close,
                    Game.GenericOpenWebSocketSession,
                    Game.GenericCloseGame,
                    _state,
                    _channelApi,
                    _guildApi,
                    _logger));
                
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
                        var waitingResult = await _state.BurstApi.WaitForGame<ChasePigPlayerState>(
                            joinResult.JoinStatus, _context, joinResult.MentionedPlayers,
                            joinResult.BotUser, "", GameName, _interactionApi, _guildApi, _logger);
                        if (!waitingResult.HasValue)
                            throw new Exception($"Failed to get waiting result for {GameName}.");

                        var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                        if (!guild.HasValue) return;

                        var (matchData, playerStates) = waitingResult.Value;
                        foreach (var player in playerStates)
                        {

                            await ChasePigGame.AddPlayerState(matchData.GameId ?? "", guild.Value, player,
                                new ChasePigInGameRequest
                                {
                                    AvatarUrl = player.AvatarUrl,
                                    ChannelId = player.TextChannel!.ID.Value,
                                    ClientType = ClientType.Discord,
                                    GameId = player.GameId,
                                    PlayerId = player.PlayerId,
                                    PlayerName = player.PlayerName,
                                    RequestType = ChasePigInGameRequestType.Deal
                                }, _state.GameStates.ChasePigGameStates.Item1,
                                _state.GameStates.ChasePigGameStates.Item2);
                            
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            
                            _ = Task.Run(() => ChasePigGame.StartListening(matchData.GameId ?? "",
                                _state.GameStates.ChasePigGameStates,
                                GameName,
                                ChasePigGameProgress.NotAvailable,
                                ChasePigGameProgress.Starting,
                                ChasePigGameProgress.Closed,
                                ChasePigGame.InGameRequestTypes,
                                ChasePigInGameRequestType.Close,
                                Game.GenericOpenWebSocketSession,
                                Game.GenericCloseGame,
                                _state,
                                _channelApi,
                                _guildApi,
                                _logger));
                        }
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
}