using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.Serializables;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.BlackJack;

#pragma warning disable CA2252
public partial class BlackJack
{
    private async Task<IResult> Join(float baseBet, params IUser?[] users)
    {
        var joinResult = await Game.GenericJoinGame(
            baseBet,
            users, GameType.BlackJack, "/black_jack/join", _state,
            _context, _interactionApi, _userApi, _logger);
        
        if (joinResult == null) return Result.FromSuccess();

        switch (joinResult.JoinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var result = await Game.GenericStartGame(
                    _context, joinResult.Reply, joinResult.InvokingMember, joinResult.BotUser,
                    joinResult.JoinStatus, GameName, "/black_jack/join/confirm",
                    joinResult.MentionedPlayers.Select(s => s.Value),
                    _state, 2, _interactionApi, _channelApi,
                    _guildApi, _logger);

                if (!result.HasValue)
                {
                    var failureResult = await _interactionApi
                        .EditOriginalInteractionResponseAsync(
                            _context.ApplicationID,
                            _context.Token,
                            ErrorMessages.HandleReactionFailed);
                    return !failureResult.IsSuccess ? Result.FromError(failureResult) : Result.FromSuccess();
                }

                var (members, matchData) = result.Value;
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                
                foreach (var member in members)
                {
                    var playerTip = await _state.BurstApi
                        .SendRawRequest<object>($"/tip/{member.User.Value.ID.Value}", ApiRequestType.Get, null)
                        .ReceiveJson<RawTip>();
                    var textChannel =
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, joinResult.BotUser, member, _guildApi,
                            _logger);
                    await AddPlayerState(matchData.GameId ?? "", guild.Value, new BlackJackPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        BetTips = Constants.StartingBet,
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel,
                        OwnTips = playerTip.Amount,
                        Order = 0
                    }, _state.GameStates);
                    _ = Task.Run(() =>
                        StartListening(matchData.GameId ?? "", _state,
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
                            Utilities.BuildGameEmbed(joinResult.InvokingMember, joinResult.BotUser, joinResult.JoinStatus, GameName, "",
                                null)
                        });

                if (!followUpResult.IsSuccess) return Result.FromError(followUpResult);
                
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                var textChannel = await _state.BurstApi.CreatePlayerChannel(guild.Value, joinResult.BotUser, joinResult.InvokingMember,
                    _guildApi, _logger);
                
                await AddPlayerState(
                    joinResult.JoinStatus.GameId ?? "",
                    guild.Value,
                    new BlackJackPlayerState
                    {
                        GameId = joinResult.JoinStatus.GameId ?? "",
                        PlayerId = joinResult.InvokingMember.User.Value.ID.Value,
                        PlayerName = joinResult.InvokingMember.GetDisplayName(),
                        TextChannel = textChannel,
                        OwnTips = joinResult.InvokerTip.Amount,
                        BetTips = Constants.StartingBet,
                        Order = 0,
                        AvatarUrl = joinResult.InvokingMember.GetAvatarUrl()
                    }, _state.GameStates
                );
                _ = Task.Run(() =>
                    StartListening(joinResult.JoinStatus.GameId ?? "", _state, _channelApi, _guildApi, _logger));
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
                        var waitingResult = await _state.BurstApi
                            .WaitForBlackJackGame(joinResult.JoinStatus, _context, joinResult.MentionedPlayers,
                                joinResult.BotUser, "",
                            _interactionApi, _guildApi,
                            _logger);
                        
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
                            
                            _ = Task.Run(() =>
                                StartListening(matchData.GameId ?? "",
                                    _state, _channelApi, _guildApi, _logger));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("WebSocket failed: {Exception}", ex);
                    }
                });
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported join status type.");
        }
        
        return Result.FromSuccess();
    }
}