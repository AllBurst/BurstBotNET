using System.Collections.Immutable;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.Serializables;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.BlackJack;

#pragma warning disable CA2252
public partial class BlackJack
{
    private async Task<IResult> Join(float baseBet, params IUser?[] users)
    {
        var mentionedPlayers = new List<Snowflake> { _context.User.ID };
        var additionalPlayers = users
            .Where(u => u != null)
            .Select(u => u!.ID)
            .ToImmutableArray();
        mentionedPlayers.AddRange(additionalPlayers);

        var playerIds = mentionedPlayers
            .Select(s => s.Value)
            .ToImmutableArray();

        var (validationResult, invokerTip) = await Game.ValidatePlayers(_context.User.ID.Value, playerIds,
            _state.BurstApi,
            _context, 1.0f, _interactionApi);
        
        if (!validationResult) return Result.FromSuccess();

        var joinResult = await Game.GenericJoinGame(
            baseBet,
            playerIds, GameType.BlackJack, "/black_jack/join", _state.BurstApi,
            _context, _interactionApi);
        
        if (joinResult == null) return Result.FromSuccess();

        var (joinStatus, reply) = joinResult.Value;

        var invokingMember = await Utilities.GetUserMember(_context, _interactionApi,
            ErrorMessages.JoinNotInGuild, _logger);
        var botUser = await Utilities.GetBotUser(_userApi, _logger);
        
        if (invokingMember == null || botUser == null) return Result.FromSuccess();

        switch (joinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var result = await Game.GenericStartGame(
                    _context, reply, invokingMember, botUser,
                    joinStatus, GameName, "/black_jack/join/confirm", playerIds,
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
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, botUser, invokingMember, _guildApi,
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
                        reply);
                if (!matchedMessageResult.IsSuccess) return Result.FromError(matchedMessageResult);

                var followUpResult = await _interactionApi
                    .CreateFollowupMessageAsync(
                        _context.ApplicationID,
                        _context.Token,
                        embeds: new[]
                        {
                            Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName, "",
                                null)
                        });

                if (!followUpResult.IsSuccess) return Result.FromError(followUpResult);
                
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                var textChannel = await _state.BurstApi.CreatePlayerChannel(guild.Value, botUser, invokingMember,
                    _guildApi, _logger);
                
                await AddPlayerState(
                    joinStatus.GameId ?? "",
                    guild.Value,
                    new BlackJackPlayerState
                    {
                        GameId = joinStatus.GameId ?? "",
                        PlayerId = invokingMember.User.Value.ID.Value,
                        PlayerName = invokingMember.GetDisplayName(),
                        TextChannel = textChannel,
                        OwnTips = invokerTip?.Amount ?? 0,
                        BetTips = Constants.StartingBet,
                        Order = 0,
                        AvatarUrl = invokingMember.GetAvatarUrl()
                    }, _state.GameStates
                );
                _ = Task.Run(() =>
                    StartListening(joinStatus.GameId ?? "", _state, _channelApi, _guildApi, _logger));
                break;
            }
            case GenericJoinStatusType.Waiting:
            {
                var waitingMessageResult = await _interactionApi
                    .EditOriginalInteractionResponseAsync(
                        _context.ApplicationID,
                        _context.Token,
                        reply);
                if (!waitingMessageResult.IsSuccess) return Result.FromError(waitingMessageResult);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await _state.BurstApi
                            .WaitForBlackJackGame(joinStatus, _context, mentionedPlayers,
                            botUser, "",
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