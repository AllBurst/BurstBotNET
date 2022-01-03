using System.Collections.Immutable;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.ChinesePoker;

#pragma warning disable CA2252
public partial class ChinesePoker
{
    private async Task<IResult> Join(
        float baseBet,
        IUser? player2 = null,
        IUser? player3 = null,
        IUser? player4 = null)
    {
        var mentionedPlayers = new List<Snowflake> { _context.User.ID };
        var additionalPlayers = new List<IUser?>
            {
                player2, player3, player4
            }
            .Where(u => u != null)
            .Select(u => u!.ID)
            .ToImmutableArray();
        mentionedPlayers.AddRange(additionalPlayers);

        var playerIds = mentionedPlayers.Select(s => s.Value).ToImmutableArray();

        var (validationResult, _) = await Game.ValidatePlayers(_context.User.ID.Value, playerIds,
            _state.BurstApi,
            _context, baseBet, _interactionApi);

        if (!validationResult) return Result.FromSuccess();

        var joinResult = await Game.GenericJoinGame(baseBet,
            playerIds, GameType.ChinesePoker, "/chinese_poker/join", _state.BurstApi,
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
                    joinStatus, GameName, "/chinese_poker/join/confirm", playerIds,
                    _state, 4, _interactionApi, _channelApi,
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
                    var textChannel =
                        await _state.BurstApi.CreatePlayerChannel(guild.Value, botUser, invokingMember, _guildApi, _logger);

                    await AddPlayerState(matchData.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel,
                        Member = member
                    }, _state.GameStates);
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
                
                await AddPlayerState(joinStatus.GameId ?? "", guild.Value, new ChinesePokerPlayerState
                {
                    AvatarUrl = invokingMember.GetAvatarUrl(),
                    GameId = joinStatus.GameId ?? "",
                    PlayerId = invokingMember.User.Value.ID.Value,
                    PlayerName = invokingMember.GetDisplayName(),
                    TextChannel = textChannel,
                    Member = invokingMember
                }, _state.GameStates);
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
                            joinStatus, _context, mentionedPlayers,
                            botUser, "", _interactionApi, _guildApi, _logger);
                        if (!waitingResult.HasValue)
                            throw new Exception($"Failed to get waiting result for {GameName}.");

                        var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                        if (!guild.HasValue) return;

                        var (matchData, playerStates) = waitingResult.Value;
                        foreach (var player in playerStates)
                        {
                            await AddPlayerState(matchData.GameId ?? "", guild.Value, player,
                                _state.GameStates);
                            _ = Task.Run(() => StartListening(matchData.GameId ?? "", _state,
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