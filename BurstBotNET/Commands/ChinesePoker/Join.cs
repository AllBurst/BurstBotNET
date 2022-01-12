using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.ChinesePoker;

using ChinesePokerGame =
    IGame<ChinesePokerGameState, RawChinesePokerGameState, ChinesePoker, ChinesePokerPlayerState,
        ChinesePokerGameProgress,
        ChinesePokerInGameRequestType>;

#pragma warning disable CA2252
public partial class ChinesePoker
{
    private async Task<IResult> Join(
        float baseBet, params IUser?[] users)
    {
        var joinResult = await Game.GenericJoinGame(
            baseBet, users, GameType.ChinesePoker, "/chinese_poker/join",
            _state, _context, _interactionApi, _userApi, _logger);

        if (joinResult == null) return Result.FromSuccess();

        switch (joinResult.JoinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var result = await Game.GenericStartGame(
                    _context, joinResult.Reply, joinResult.InvokingMember, joinResult.BotUser,
                    joinResult.JoinStatus, GameName, "/chinese_poker/join/confirm",
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

                    await AddPlayerState(matchData.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel
                    }, _state.GameStates);
                    _ = Task.Run(() => ChinesePokerGame.StartListening(
                        matchData.GameId ?? "",
                        _state.GameStates.ChinesePokerGameStates,
                        GameName,
                        ChinesePokerGameProgress.NotAvailable,
                        ChinesePokerGameProgress.Starting,
                        ChinesePokerGameProgress.Closed,
                        ChinesePokerGame.InGameRequestTypes,
                        ChinesePokerInGameRequestType.Close,
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
                
                await AddPlayerState(joinResult.JoinStatus.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                {
                    AvatarUrl = joinResult.InvokingMember.GetAvatarUrl(),
                    GameId = joinResult.JoinStatus.GameId ?? "",
                    PlayerId = joinResult.InvokingMember.User.Value.ID.Value,
                    PlayerName = joinResult.InvokingMember.GetDisplayName(),
                    TextChannel = textChannel
                }, _state.GameStates);
                _ = Task.Run(() => ChinesePokerGame.StartListening(
                    joinResult.JoinStatus.GameId ?? "",
                    _state.GameStates.ChinesePokerGameStates,
                    GameName,
                    ChinesePokerGameProgress.NotAvailable,
                    ChinesePokerGameProgress.Starting,
                    ChinesePokerGameProgress.Closed,
                    ChinesePokerGame.InGameRequestTypes,
                    ChinesePokerInGameRequestType.Close,
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
                        var waitingResult = await _state.BurstApi.WaitForGame<ChinesePokerPlayerState>(
                            joinResult.JoinStatus, _context, joinResult.MentionedPlayers,
                            joinResult.BotUser, "", GameName, _interactionApi, _guildApi, _logger);
                        if (!waitingResult.HasValue)
                            throw new Exception($"Failed to get waiting result for {GameName}.");

                        var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                        if (!guild.HasValue) return;

                        var (matchData, playerStates) = waitingResult.Value;
                        foreach (var player in playerStates)
                        {
                            await AddPlayerState(matchData.GameId ?? "", guild.Value, player,
                                _state.GameStates);

                            await Task.Delay(TimeSpan.FromSeconds(1));
                            
                            _ = Task.Run(() => ChinesePokerGame.StartListening(
                                matchData.GameId ?? "",
                                _state.GameStates.ChinesePokerGameStates,
                                GameName,
                                ChinesePokerGameProgress.NotAvailable,
                                ChinesePokerGameProgress.Starting,
                                ChinesePokerGameProgress.Closed,
                                ChinesePokerGame.InGameRequestTypes,
                                ChinesePokerInGameRequestType.Close,
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