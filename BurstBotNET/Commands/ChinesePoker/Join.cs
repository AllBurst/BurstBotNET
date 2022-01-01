using System.Collections.Immutable;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.ChinesePoker;

#pragma warning disable CA2252
public partial class ChinesePoker
{
    private async Task<IResult> Join(
        float baseBet,
        IUser? playerTwo = null,
        IUser? playerThree = null,
        IUser? playerFour = null)
    {
        var mentionedPlayers = new List<ulong> { _context.User.ID.Value };
        var additionalPlayers = new List<IUser?>
            {
                playerTwo, playerThree, playerFour
            }
            .Where(u => u != null)
            .Select(u => u!.ID.Value)
            .ToImmutableArray();
        
        mentionedPlayers.AddRange(additionalPlayers);

        var getTipTasks = mentionedPlayers
            .Select(async p =>
            {
                var response = await _state.BurstApi.SendRawRequest<object>($"/tip/{p}", ApiRequestType.Get, null);
                if (!response.ResponseMessage.IsSuccessStatusCode)
                    return null;

                var playerTip = await response.GetJsonAsync<RawTip>();
                
                return playerTip.Amount < baseBet ? null : playerTip;
            });

        var playerTips = await Task.WhenAll(getTipTasks);
        var hasInvalidPlayer = playerTips.Any(tip => tip == null);

        if (hasInvalidPlayer)
        {
            var errorResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    ErrorMessages.InvalidPlayer);
            return Result.FromError(errorResult);
        }

        var joinRequest = new GenericJoinRequest
        {
            ClientType = ClientType.Discord,
            GameType = GameType.ChinesePoker,
            PlayerIds = mentionedPlayers
        };
        var joinResponse = await _state.BurstApi.SendRawRequest("/chinese_poker/join", ApiRequestType.Post, joinRequest);
        var playerCount = mentionedPlayers.Count;
        var unit = playerCount > 1 ? "players" : "player";

        var (joinStatus, reply) = BurstApi.HandleMatchGameHttpStatuses(joinResponse, unit, GameType.ChinesePoker);
        if (joinStatus == null)
        {
            var errorResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    reply);
            return Result.FromError(errorResult);
        }

        var invokingMember = await Utilities.GetUserMember(_context, _interactionApi,
            ErrorMessages.JoinNotInGuild, _logger);
        var botUser = await Utilities.GetBotUser(_userApi, _logger);

        if (invokingMember == null || botUser == null) return Result.FromSuccess();

        switch (joinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                var startResult = await _interactionApi
                    .EditOriginalInteractionResponseAsync(
                        _context.ApplicationID,
                        _context.Token,
                        reply,
                        new[]
                        {
                            Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName,
                                "", null)
                        });

                if (!startResult.IsSuccess) return Result.FromError(startResult);

                var reactionResult = await BurstApi
                    .HandleStartGameReactions(
                        GameName, _context, startResult.Entity, invokingMember, botUser, joinStatus,
                        mentionedPlayers, "/chinese_poker/join/confirm", _state,
                        _channelApi, _interactionApi, _guildApi, _logger, 4);

                if (!reactionResult.HasValue)
                {
                    var failureResult = await _interactionApi
                        .EditOriginalInteractionResponseAsync(
                            _context.ApplicationID,
                            _context.Token,
                            ErrorMessages.HandleReactionFailed);

                    return Result.FromError(failureResult);
                }

                var (members, matchData) = reactionResult.Value;

                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                
                foreach (var member in members)
                {
                    var textChannel =
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, botUser, invokingMember, _guildApi, _logger);

                    await AddChinesePokerPlayerState(matchData.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel,
                        Member = member
                    }, _state.GameStates, baseBet);
                    _ = Task.Run(() => StartListening(matchData.GameId ?? "", _state,
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

                var textChannel = await _state.BurstApi.CreatePlayerChannel(
                    guild.Value,
                    botUser,
                    invokingMember,
                    _guildApi,
                    _logger);
                
                await AddChinesePokerPlayerState(joinStatus.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                {
                    AvatarUrl = invokingMember.GetAvatarUrl(),
                    GameId = joinStatus.GameId ?? "",
                    PlayerId = invokingMember.User.Value.ID.Value,
                    PlayerName = invokingMember.GetDisplayName(),
                    TextChannel = textChannel,
                    Member = invokingMember
                }, _state.GameStates, baseBet);
                _ = Task.Run(() => StartListening(joinStatus.GameId ?? "", _state,
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
                        reply);
                if (!waitingMessageResult.IsSuccess) return Result.FromError(waitingMessageResult);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await _state.BurstApi.WaitForChinesePokerGame(
                            joinStatus, _context, invokingMember,
                            botUser, "", _interactionApi, _guildApi, _logger);
                        if (!waitingResult.HasValue)
                            throw new Exception($"Failed to get waiting result for {GameName}.");

                        var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                        if (!guild.HasValue) return;

                        var (matchData, playerState) = waitingResult.Value;
                        await AddChinesePokerPlayerState(matchData.GameId ?? "", guild.Value, playerState,
                            _state.GameStates, baseBet);
                        _ = Task.Run(() => StartListening(matchData.GameId ?? "", _state,
                            _channelApi,
                            _guildApi,
                            _logger));
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