using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackInteractionGroup : InteractionGroup, IHelpInteraction
{
    public const string DrawCustomId = "draw";
    public const string StandCustomId = "stand";
    public const string CallCustomId = "call";
    public const string FoldCustomId = "fold";
    public const string RaiseCustomId = "raise";
    public const string AllInCustomId = "allin";
    public const string HelpCustomId = "blackjack_help";
    
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<BlackJackInteractionGroup> _logger;

    public BlackJackInteractionGroup(
        InteractionContext context,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        ILogger<BlackJackInteractionGroup> logger)
    {
        _context = context;
        _state = state;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _logger = logger;
    }

    public static async Task<IResult> SendRaiseData(
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
        await gameState.RequestChannel!.Writer.WriteAsync(sendData, ct);
        await Utilities.Utilities.DisableComponents(playerState.MessageReference!, true, channelApi, logger, ct);

        return Result.FromSuccess();
    }
    
    public static async Task<Result> ShowHelpMenu(
        InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().BlackJack;

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent(CustomIDHelpers.CreateSelectMenuID("blackjack_help_selections"), new[]
                {
                    new SelectOption(localization.Rules, "helprule", localization.Rules, new PartialEmoji(Name: "🥷"),
                        false),
                    new SelectOption(localization.GameFlow, "helpflow", localization.GameFlow,
                        new PartialEmoji(Name: "🌫️"), false),
                    new SelectOption(localization.Draw, "helpdraw", localization.Draw, new PartialEmoji(Name: "🎴"),
                        false),
                    new SelectOption(localization.Stand, "helpstand", localization.Stand, new PartialEmoji(Name: "😑"),
                        false),
                    new SelectOption(localization.Call, "helpcall", localization.Call, new PartialEmoji(Name: "🤔"),
                        false),
                    new SelectOption(localization.Fold, "helpfold", localization.Fold, new PartialEmoji(Name: "😫"),
                        false),
                    new SelectOption(localization.Raise, "helpraise", localization.Raise, new PartialEmoji(Name: "🤑"),
                        false),
                    new SelectOption(localization.AllIn, "helpallin", localization.AllIn, new PartialEmoji(Name: "😈"),
                        false)
                }, localization.ShowHelp, 0, 1, false)
            })
        };

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.About,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    [Button(DrawCustomId)]
    public async Task<IResult> Draw() => await HandleInteractionAsync(_context.User, DrawCustomId);

    [Button(StandCustomId)]
    public async Task<IResult> Stand() => await HandleInteractionAsync(_context.User, StandCustomId);

    [Button(CallCustomId)]
    public async Task<IResult> Call() => await HandleInteractionAsync(_context.User, CallCustomId);

    [Button(RaiseCustomId)]
    public async Task<IResult> Raise() => await HandleInteractionAsync(_context.User, RaiseCustomId);

    [Button(FoldCustomId)]
    public async Task<IResult> Fold() => await HandleInteractionAsync(_context.User, FoldCustomId);

    [Button(AllInCustomId)]
    public async Task<IResult> AllIn() => await HandleInteractionAsync(_context.User, AllInCustomId);

    [Button(HelpCustomId)]
    public async Task<IResult> BlackJackHelp() => await HandleInteractionAsync(_context.User, HelpCustomId);

    [SelectMenu("blackjack_help_selections")]
    public async Task<IResult> BlackJackSelection(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, "blackjack_help_selections", values);

    private async Task<IResult> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
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
            case DrawCustomId:
                return await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Draw, ct);
            case StandCustomId:
                return await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Stand, ct);
            case FoldCustomId:
                return await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Fold, ct);
            case CallCustomId:
                return await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Call, ct);
            case RaiseCustomId:
                return await HandleRaise(playerState);
            case AllInCustomId:
            {
                var remainingTips = playerState.OwnTips - playerState.BetTips - gameState.HighestBet;
                return await SendRaiseData(gameState, playerState, (int) remainingTips, _channelApi, _logger, ct);
            }
            case HelpCustomId:
                return await ShowHelpMenu(_context, _state, _interactionApi);
        }

        return Result.FromSuccess();
    }

    private async Task<IResult> HandleInteractionAsync(IUser user, string customId, IEnumerable<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var _);
        if (!hasMessage) return Result.FromSuccess();

        if (_state.GameStates.BlackJackGameStates.Item2.Contains(_context.ChannelID))
        {
            var _ = Utilities.Utilities
                .GetGameState<BlackJackGameState, BlackJackPlayerState, BlackJackGameProgress>(user,
                    _state.GameStates.BlackJackGameStates.Item1,
                    _state.GameStates.BlackJackGameStates.Item2,
                    _context,
                    out var playerState);
            if (playerState == null) return Result.FromSuccess();
        }

        var sanitizedCustomId = customId.Trim();

        if (!string.Equals(sanitizedCustomId, "blackjack_help_selections")) return Result.FromSuccess();

        return await ShowHelpTexts(values, ct);
    }

    private async Task<IResult> ShowHelpTexts(IEnumerable<string> values, CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;

        var selection = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selection)) return Result.FromSuccess();

        var getHelpResult = localization.CommandList.TryGetValue(selection, out var helpText);

        if (!getHelpResult) return Result.FromSuccess();

        var texts = new List<string>();
        if (helpText!.Length <= 2000)
            texts.Add(helpText);
        else
        {
            texts.Add(helpText[..2000]);
            texts.Add(helpText[2000..]);
        }

        foreach (var str in texts)
        {
            var result = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                    str, ct: ct);
            if (!result.IsSuccess)
                return Result.FromError(result);

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return Result.FromSuccess();
    }

    private async Task<IResult> SendGenericData(BlackJackGameState gameState,
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
        await gameState.RequestChannel!.Writer.WriteAsync(sendData, ct);
        await Utilities.Utilities.DisableComponents(playerState.MessageReference!, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }

    private async Task<IResult> HandleRaise(BlackJackPlayerState playerState)
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;
        playerState.IsRaising = true;

        var result = await _channelApi
            .CreateMessageAsync(playerState.TextChannel!.ID, localization.RaisePrompt);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}