using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackButtonEntity : IButtonInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<BlackJackButtonEntity> _logger;

    public BlackJackButtonEntity(
        InteractionContext context,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        ILogger<BlackJackButtonEntity> logger)
    {
        _context = context;
        _state = state;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _logger = logger;
    }

    public static async Task SendRaiseData(
        BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        int raiseBet,
        IDiscordRestChannelAPI channelApi,
        ILogger logger,
        CancellationToken ct)
    {
        var sendData = new Tuple<ulong, byte[]>(playerState.PlayerId, JsonSerializer.SerializeToUtf8Bytes(
            new BlackJackInGameRequest
            {
                RequestType = BlackJackInGameRequestType.Raise,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                Bets = raiseBet
            }));
        await gameState.Channel!.Writer.WriteAsync(sendData);
        await Utilities.Utilities.DisableComponents(playerState.MessageReference!, channelApi, logger, ct);
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        var isValidButton = customId is "draw" or "stand" or "call" or "raise" or "fold" or "allin" or "blackjack_help";
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(isValidButton);
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        var gameState = Utilities.Utilities
            .GetGameState<BlackJackGameState, BlackJackPlayerState, BlackJackGameProgress>(user,
                _state.GameStates.BlackJackGameStates.Item1,
                _state.GameStates.BlackJackGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState?.TextChannel == null) return Result.FromSuccess();

        playerState.MessageReference = message;

        switch (sanitizedCustomId)
        {
            case "draw":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Draw, ct);
                break;
            case "stand":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Stand, ct);
                break;
            case "fold":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Fold, ct);
                break;
            case "call":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Call, ct);
                break;
            case "raise":
                return await HandleRaise(playerState);
            case "allin":
            {
                var remainingTips = playerState.OwnTips - playerState.BetTips - gameState.HighestBet;
                await SendRaiseData(gameState, playerState, (int)remainingTips, _channelApi, _logger, ct);
                break;
            }
            case "blackjack_help":
                return await ShowHelpMenu();
        }

        return Result.FromSuccess();
    }
    
    private async Task SendGenericData(BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        BlackJackInGameRequestType requestType,
        CancellationToken ct)
    {
        var sendData = new Tuple<ulong, byte[]>(playerState.PlayerId, JsonSerializer.SerializeToUtf8Bytes(
            new BlackJackInGameRequest
            {
                RequestType = requestType,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId
            }));
        await gameState.Channel!.Writer.WriteAsync(sendData);
        await Utilities.Utilities.DisableComponents(playerState.MessageReference!, _channelApi, _logger, ct);
    }

    private async Task<Result> HandleRaise(BlackJackPlayerState playerState)
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;
        playerState.IsRaising = true;

        var result = await _channelApi
            .CreateMessageAsync(playerState.TextChannel!.ID, localization.RaisePrompt);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    private async Task<Result> ShowHelpMenu()
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("blackjack_help_selections", new[]
                {
                    new SelectOption(localization.Rules, "helprule", localization.Rules, new PartialEmoji(Name: "🥷"), false),
                    new SelectOption(localization.GameFlow, "helpflow", localization.GameFlow, new PartialEmoji(Name: "🌫️"), false),
                    new SelectOption(localization.Draw, "helpdraw", localization.Draw, new PartialEmoji(Name: "🎴"), false),
                    new SelectOption(localization.Stand, "helpstand", localization.Stand, new PartialEmoji(Name: "😑"), false),
                    new SelectOption(localization.Call, "helpcall", localization.Call, new PartialEmoji(Name: "🤔"), false),
                    new SelectOption(localization.Fold, "helpfold", localization.Fold, new PartialEmoji(Name: "😫"), false),
                    new SelectOption(localization.Raise, "helpraise", localization.Raise, new PartialEmoji(Name: "🤑"), false),
                    new SelectOption(localization.AllIn, "helpallin", localization.AllIn, new PartialEmoji(Name: "😈"), false)
                }, localization.ShowHelp, 0, 1, false)
            })
        };

        var result = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                localization.ShowHelp,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}