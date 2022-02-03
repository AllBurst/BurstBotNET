using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.BlackJack;

using BlackJackGame =
    IGame<BlackJackGameState, RawBlackJackGameState, BlackJack, BlackJackPlayerState, BlackJackGameProgress,
        BlackJackInGameRequestType>;

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
                var startGameData = new GenericStartGameData
                {
                    BotUser = joinResult.BotUser,
                    ChannelApi = _channelApi,
                    ConfirmationEndpoint = "/black_jack/join/confirm",
                    Context = _context,
                    GameName = GameName,
                    GuildApi = _guildApi,
                    InteractionApi = _interactionApi,
                    InvokingMember = joinResult.InvokingMember,
                    JoinStatus = joinResult.JoinStatus,
                    Logger = _logger,
                    State = _state,
                    PlayerIds = joinResult.MentionedPlayers.Select(s => s.Value),
                    MinPlayerCount = 2,
                    Reply = joinResult.Reply
                };
                
                var result = await Game.GenericStartGame(startGameData);

                if (!result.HasValue) return Result.FromSuccess();

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

                    var playerState = new BlackJackPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(),
                        BetTips = Constants.StartingBet,
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.User.Value.ID.Value,
                        PlayerName = member.GetDisplayName(),
                        TextChannel = textChannel,
                        OwnTips = playerTip.Amount,
                        Order = 0
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
                            Utilities.BuildGameEmbed(joinResult.InvokingMember, joinResult.BotUser, joinResult.JoinStatus, GameName, "",
                                null)
                        });

                if (!followUpResult.IsSuccess) return Result.FromError(followUpResult);
                
                var guild = await Utilities.GetGuildFromContext(_context, _interactionApi, _logger);
                if (!guild.HasValue) return Result.FromSuccess();
                var textChannel = await _state.BurstApi.CreatePlayerChannel(guild.Value, joinResult.BotUser, joinResult.InvokingMember,
                    _guildApi, _logger);

                var playerState = new BlackJackPlayerState
                {
                    GameId = joinResult.JoinStatus.GameId ?? "",
                    PlayerId = joinResult.InvokingMember.User.Value.ID.Value,
                    PlayerName = joinResult.InvokingMember.GetDisplayName(),
                    TextChannel = textChannel,
                    OwnTips = joinResult.InvokerTip.Amount,
                    BetTips = Constants.StartingBet,
                    Order = 0,
                    AvatarUrl = joinResult.InvokingMember.GetAvatarUrl()
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
                        var waitingResult = await _state.BurstApi
                            .WaitForBlackJackGame(joinResult.JoinStatus, _context, joinResult.MentionedPlayers,
                                joinResult.BotUser, "",
                                _state.AmqpService,
                                _interactionApi, _guildApi,
                                _logger);

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
            default:
                throw new InvalidOperationException("Unsupported join status type.");
        }
        
        return Result.FromSuccess();
    }

    public async Task AddPlayerStateAndStartListening(GenericJoinStatus? joinStatus, BlackJackPlayerState playerState, Snowflake guild)
    {
        await BlackJackGame.AddPlayerState(joinStatus?.GameId ?? "", guild, playerState,
            new BlackJackInGameRequest
            {
                GameId = joinStatus?.GameId ?? "",
                AvatarUrl = playerState.AvatarUrl,
                PlayerId = playerState.PlayerId,
                ChannelId = playerState.TextChannel!.ID.Value,
                PlayerName = playerState.PlayerName,
                OwnTips = playerState.OwnTips,
                ClientType = ClientType.Discord,
                RequestType = BlackJackInGameRequestType.Deal,
            }, _state.GameStates.BlackJackGameStates.Item1,
            _state.GameStates.BlackJackGameStates.Item2);
                            
        await Task.Delay(TimeSpan.FromSeconds(1));
                            
        _ = Task.Run(() => BlackJackGame.StartListening(
            joinStatus?.GameId ?? "",
            _state.GameStates.BlackJackGameStates,
            GameName,
            BlackJackGameProgress.NotAvailable,
            BlackJackGameProgress.Starting,
            BlackJackGameProgress.Closed,
            BlackJackGame.InGameRequestTypes,
            BlackJackInGameRequestType.Close,
            Game.GenericOpenWebSocketSession,
            Game.GenericCloseGame,
            _state,
            _channelApi,
            _guildApi,
            _logger));
    }
}