using System.Text.Json;
using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.NinetyNine.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;
using Remora.Discord.API.Objects;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineInteractionGroup : InteractionGroup, IHelpButtonEntity
{
    public const string PlayerSelectionCustomId = "ninety_nine_player_selection";
    public const string UserSelectionCustomId = "ninety_nine_user_selection";
    public const string PlusTenCustomId = "plus10";
    public const string PlusTwentyCustomId = "plus20";
    public const string PlusOneCustomId = "plus1";
    public const string PlusElevenCustomId = "plus11";
    public const string PlusFourteenCustomId = "plus14";
    public const string MinusTenCustomId = "minus10";
    public const string MinusTwentyCustomId = "minus20";
    public const string ConfirmCustomId = "ninety_nine_confirm";
    public const string HelpTaiwaneseCustomId = "ninety_nine_help_Taiwanese";
    public const string HelpIcelandicCustomId = "ninety_nine_help_Icelandic";
    public const string HelpStandardCustomId = "ninety_nine_help_Standard";
    public const string HelpBloodyCustomId = "ninety_nine_help_Bloody";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<NinetyNineInteractionGroup> _logger;

    private readonly string[] _validSelectMenuCustomIds =
    {
        UserSelectionCustomId,
        PlayerSelectionCustomId
    };
    
    private readonly string[] _validButtonCustomIds =
    {
        PlusTenCustomId,
        PlusTwentyCustomId,
        PlusOneCustomId,
        PlusElevenCustomId,
        PlusFourteenCustomId,
        MinusTenCustomId,
        MinusTwentyCustomId,
        ConfirmCustomId,
        HelpTaiwaneseCustomId,
        HelpIcelandicCustomId,
        HelpStandardCustomId,
        HelpBloodyCustomId
    };

    public NinetyNineInteractionGroup(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<NinetyNineInteractionGroup> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state,
        IDiscordRestInteractionAPI interactionApi)
        => await ShowHelpMenu(NinetyNineVariation.Taiwanese, context, state, interactionApi);

    public static async Task<Result> ShowHelpMenu(NinetyNineVariation variation, InteractionContext context,
        State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().NinetyNine;

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                variation switch
                {
                    NinetyNineVariation.Taiwanese => localization.CommandList["generalTaiwanese"],
                    NinetyNineVariation.Icelandic => localization.CommandList["generalIcelandic"],
                    NinetyNineVariation.Standard => localization.CommandList["generalStandard"],
                    NinetyNineVariation.Bloody => localization.CommandList["generalBloody"],
                    _ => localization.CommandList["generalTaiwanese"]
                });

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    [SelectMenu(UserSelectionCustomId)]
    public async Task<IResult> SelectUser(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, UserSelectionCustomId, values);

    [Button(PlusTenCustomId)]
    public async Task<IResult> Plus10() => await HandleInteractionAsync(_context.User, PlusTenCustomId);
    
    [Button(PlusTwentyCustomId)]
    public async Task<IResult> Plus20() => await HandleInteractionAsync(_context.User, PlusTwentyCustomId);
    
    [Button(PlusOneCustomId)]
    public async Task<IResult> Plus1() => await HandleInteractionAsync(_context.User, PlusOneCustomId);
    
    [Button(PlusElevenCustomId)]
    public async Task<IResult> Plus11() => await HandleInteractionAsync(_context.User, PlusElevenCustomId);
    
    [Button(PlusFourteenCustomId)]
    public async Task<IResult> Plus14() => await HandleInteractionAsync(_context.User, PlusFourteenCustomId);
    
    [Button(MinusTenCustomId)]
    public async Task<IResult> Minus10() => await HandleInteractionAsync(_context.User, MinusTenCustomId);
    
    [Button(MinusTwentyCustomId)]
    public async Task<IResult> Minus20() => await HandleInteractionAsync(_context.User, MinusTwentyCustomId);
    
    [Button(ConfirmCustomId)]
    public async Task<IResult> Confirm() => await HandleInteractionAsync(_context.User, ConfirmCustomId);
    
    [Button(HelpTaiwaneseCustomId)]
    public async Task<IResult> HelpTaiwanese() => await HandleInteractionAsync(_context.User, HelpTaiwaneseCustomId);
    
    [Button(HelpIcelandicCustomId)]
    public async Task<IResult> HelpIcelandic() => await HandleInteractionAsync(_context.User, HelpIcelandicCustomId);
    
    [Button(HelpStandardCustomId)]
    public async Task<IResult> HelpStandard() => await HandleInteractionAsync(_context.User, HelpStandardCustomId);
    
    [Button(HelpBloodyCustomId)]
    public async Task<IResult> HelpBloody() => await HandleInteractionAsync(_context.User, HelpBloodyCustomId);
    
    private static IMessageComponent[] BuildPlayerSelectMenu(Card card, NinetyNineGameState gameState, NinetyNinePlayerState playerState, NinetyNineLocalization localization)
    {
        var playerNameList = gameState.Players
            .Where(p => p.Value.PlayerId != playerState.PlayerId && !gameState.BurstPlayers.Contains(p.Value.PlayerId))
            .Where(p =>
            {
                var (_, pState) = p;
                return card.Number != 5 || pState.PassTimes == 0;
            })
            .Select(p => new SelectOption(p.Value.PlayerName, p.Key.ToString()));

        var selectMenu = new SelectMenuComponent(PlayerSelectionCustomId,
            playerNameList.ToImmutableArray(),
            localization.SelectPlayerMessage, 1, 1);

        return new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                selectMenu
            })
        };
    }
    
    private static IMessageComponent[] BuildPlusMinusButtons(int number, int currentTotal, NinetyNineLocalization localization)
    {
        var plusButton = new ButtonComponent(ButtonComponentStyle.Success, localization.Plus,
            new PartialEmoji(Name: "➕"), CustomIDHelpers.CreateButtonID($"plus{number}"));
        var minusButton = new ButtonComponent(ButtonComponentStyle.Danger, localization.Minus,
            new PartialEmoji(Name: "➖"), CustomIDHelpers.CreateButtonID($"minus{number}"));

        var buttonComponents = new List<IMessageComponent>();

        if (currentTotal + number <= 99)
            buttonComponents.Add(plusButton);
        if (currentTotal - number >= 0)
            buttonComponents.Add(minusButton);
                    
        return new IMessageComponent[]
        {
            new ActionRowComponent(buttonComponents)
        };
    }
    
    private async Task<IResult> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        var gameState = Utilities.Utilities
            .GetGameState<NinetyNineGameState, NinetyNinePlayerState, NinetyNineGameProgress>(user,
                _state.GameStates.NinetyNineGameStates.Item1,
                _state.GameStates.NinetyNineGameStates.Item2,
                _context,
                out var playerState);

        if (playerState == null) return Result.FromSuccess();

        var sanitizedCustomId = customId.Trim();

        return sanitizedCustomId switch
        {
            UserSelectionCustomId => await Play(message!, gameState, playerState, values, ct),
            var id when string.Equals(id, PlayerSelectionCustomId) => await PlayFive(message!, gameState, playerState,
                values, ct),
            _ => Result.FromSuccess()
        };
    }
    
    private async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        var gameState = Utilities.Utilities
            .GetGameState<NinetyNineGameState, NinetyNinePlayerState, NinetyNineGameProgress>(user,
                _state.GameStates.NinetyNineGameStates.Item1,
                _state.GameStates.NinetyNineGameStates.Item2,
                _context,
                out var playerState);

        if (playerState?.TextChannel == null) return Result.FromSuccess();

        var sanitizedCustomId = customId.Trim();
        return sanitizedCustomId switch
        {
            PlusTenCustomId or PlusTwentyCustomId => await PlusOrMinus(message!, gameState, playerState,
                NinetyNineInGameAdjustmentType.Plus, ct),
            MinusTenCustomId or MinusTwentyCustomId => await PlusOrMinus(message!, gameState, playerState,
                NinetyNineInGameAdjustmentType.Minus, ct),
            PlusOneCustomId or PlusElevenCustomId or PlusFourteenCustomId => await PlusOrMinus(message!, gameState, playerState,
                sanitizedCustomId switch
                {
                    PlusOneCustomId => NinetyNineInGameAdjustmentType.One,
                    PlusElevenCustomId => NinetyNineInGameAdjustmentType.Eleven,
                    PlusFourteenCustomId => NinetyNineInGameAdjustmentType.Fourteen,
                    _ => NinetyNineInGameAdjustmentType.One
                }, ct),
            HelpTaiwaneseCustomId => await ShowHelpMenu(NinetyNineVariation.Taiwanese, _context, _state,
                _interactionApi),
            HelpIcelandicCustomId => await ShowHelpMenu(NinetyNineVariation.Icelandic, _context, _state,
                _interactionApi),
            HelpStandardCustomId => await ShowHelpMenu(NinetyNineVariation.Standard, _context, _state,
                _interactionApi),
            HelpBloodyCustomId => await ShowHelpMenu(NinetyNineVariation.Bloody, _context, _state,
                _interactionApi),
            ConfirmCustomId => await GiveUp(message!, gameState, playerState, ct),
            _ => Result.FromSuccess()
        };
    }

    private async Task<Result> Play(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        return gameState.Variation switch
        {
            NinetyNineVariation.Taiwanese => await PlayTaiwanese(message, gameState, playerState, values, ct),
            NinetyNineVariation.Icelandic => await PlayIcelandic(message, gameState, playerState, values, ct),
            NinetyNineVariation.Standard => await PlayStandard(message, gameState, playerState, values, ct),
            _ => await PlayTaiwanese(message, gameState, playerState, values, ct)
        };
    }

    private async Task<Result> PlayStandard(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCard = Utilities.Utilities.ExtractCard(values);
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        switch (extractedCard.Number)
        {
            case 1:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);
                
                var plusOne = new ButtonComponent(ButtonComponentStyle.Success,
                    localization.PlusSpecific.Replace("{number}", 1.ToString(CultureInfo.InvariantCulture)),
                    new PartialEmoji(Name: "➕"), CustomIDHelpers.CreateButtonID(PlusOneCustomId));
                var plusEleven = new ButtonComponent(ButtonComponentStyle.Success,
                    localization.PlusSpecific.Replace("{number}", 11.ToString(CultureInfo.InvariantCulture)),
                    new PartialEmoji(Name: "➕"), CustomIDHelpers.CreateButtonID(PlusElevenCustomId));
                    
                var buttonComponents = new List<IMessageComponent>();

                if (gameState.CurrentTotal + 1 <= 99)
                    buttonComponents.Add(plusOne);
                if (gameState.CurrentTotal + 11 <= 99)
                    buttonComponents.Add(plusEleven);
                    
                var component = new IMessageComponent[]
                {
                    new ActionRowComponent(buttonComponents)
                };
                    
                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOneOrEleven,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
            case 10:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);
                
                var component = BuildPlusMinusButtons(10, gameState.CurrentTotal, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOrMinusMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
        }
        
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayCards = new[] { extractedCard },
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        return Result.FromSuccess();
    }

    private async Task<Result> PlayIcelandic(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCards = values
            .Select(Utilities.Utilities.ExtractCard)
            .ToImmutableArray();
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        if (extractedCards.Length > 1 && extractedCards.Any(c => c.Number == 12))
        {
            var sendResult = await _channelApi
                .CreateMessageAsync(
                    playerState.TextChannel!.ID,
                    localization.NotOnlyQueen,
                    ct: ct);
            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        foreach (var card in extractedCards)
        {
            playerState.TemporaryCards.Enqueue(card);
            
            switch (card.Number)
            {
                case 1:
                {
                    playerState.ResponsesInWait++;

                    var plusOne = new ButtonComponent(ButtonComponentStyle.Success,
                        localization.PlusSpecific.Replace("{number}", 1.ToString(CultureInfo.InvariantCulture)),
                        new PartialEmoji(Name: "➕"), CustomIDHelpers.CreateButtonID(PlusOneCustomId));
                    var plusFourteen = new ButtonComponent(ButtonComponentStyle.Success,
                        localization.PlusSpecific.Replace("{number}", 14.ToString(CultureInfo.InvariantCulture)),
                        new PartialEmoji(Name: "➕"), CustomIDHelpers.CreateButtonID(PlusFourteenCustomId));
                    
                    var buttonComponents = new List<IMessageComponent>();

                    if (gameState.CurrentTotal + 1 <= 99)
                        buttonComponents.Add(plusOne);
                    if (gameState.CurrentTotal + 14 <= 99)
                        buttonComponents.Add(plusFourteen);
                    
                    var component = new IMessageComponent[]
                    {
                        new ActionRowComponent(buttonComponents)
                    };
                    
                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.PlusOneOrFourteen,
                            components: component,
                            ct: ct);

                    if (sendResult.IsSuccess) continue;

                    _logger.LogError("Failed to send buttons for Ace: {Reason}, inner: {Inner}",
                        sendResult.Error.Message, sendResult.Inner);

                    break;
                }
                case 10:
                {
                    playerState.ResponsesInWait++;
                    
                    var component = BuildPlusMinusButtons(10, gameState.CurrentTotal, localization);

                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.PlusOrMinusMessage,
                            components: component,
                            ct: ct);

                    if (sendResult.IsSuccess) continue;

                    _logger.LogError("Failed to send select menu for Ten: {Reason}, inner: {Inner}",
                        sendResult.Error.Message, sendResult.Inner);

                    break;
                }
            }
        }

        if (playerState.ResponsesInWait > 0) return Result.FromSuccess();
        
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayCards = playerState.TemporaryCards,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        playerState.TemporaryCards.Clear();

        return Result.FromSuccess();
    }

    private async Task<Result> PlayTaiwanese(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCard = Utilities.Utilities.ExtractCard(values);
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        switch (extractedCard.Number)
        {
            case 5:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);

                var component = BuildPlayerSelectMenu(extractedCard, gameState, playerState, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.SelectPlayerMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
            case 10 or 12:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);

                var count = extractedCard.Number == 12 ? 20 : 10;

                var component = BuildPlusMinusButtons(count, gameState.CurrentTotal, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOrMinusMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
        }

        if (gameState.Variation == NinetyNineVariation.Bloody)
        {
            switch (extractedCard.Number)
            {
                case 1:
                {
                    if (extractedCard.Suit == Suit.Spade) break;
                    
                    playerState.TemporaryCards.Enqueue(extractedCard);
                    var component = BuildPlayerSelectMenu(extractedCard, gameState, playerState, localization);

                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.SelectPlayerMessage,
                            components: component,
                            ct: ct);

                    return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
                }
                case 7:
                {
                    playerState.TemporaryCards.Enqueue(extractedCard);
                    
                    var playerNameList = gameState.Players
                        .Where(p =>
                        {
                            var (pId, pState) = p;
                            var notSelf = pId != playerState.PlayerId;
                            var notBurst = !gameState.BurstPlayers.Contains(pId);
                            var hasCards = !pState.Cards.IsEmpty;
                            var notDrawn = !pState.IsDrawn;
                            
                            return notSelf && notBurst && hasCards && notDrawn;
                        })
                        .Select(p => new SelectOption(p.Value.PlayerName, p.Key.ToString()));

                    var selectMenu = new SelectMenuComponent(PlayerSelectionCustomId,
                        playerNameList.ToImmutableArray(),
                        localization.SelectPlayerMessage, 1, 1);

                    var component = new IMessageComponent[]
                    {
                        new ActionRowComponent(new[]
                        {
                            selectMenu
                        })
                    };
                    
                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.SelectPlayerMessage,
                            components: component,
                            ct: ct);

                    return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
                }
            }
        }

        var currentPlayer = gameState
            .Players
            .First(p => p.Value.Order == gameState.CurrentPlayerOrder)
            .Value;

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            currentPlayer.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = currentPlayer.PlayerId,
                PlayCards = new[] { extractedCard },
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        return Result.FromSuccess();
    }

    private async Task<Result> PlayFive(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var nextPlayerId = ulong.Parse(values.First());

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            nextPlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayCards = playerState.TemporaryCards,
                PlayerId = playerState.PlayerId,
                SpecifiedPlayer = nextPlayerId,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        playerState.TemporaryCards.Clear();
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
    
    

    private async Task<Result> PlusOrMinus(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        NinetyNineInGameAdjustmentType plusOrMinus,
        CancellationToken ct)
    {
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        switch (gameState.Variation)
        {
            case NinetyNineVariation.Icelandic:
            {
                if (playerState.ResponsesInWait > 0)
                {
                    playerState.ResponsesInWait--;
                    playerState.TemporaryAdjustments.Enqueue(plusOrMinus);
                    if (playerState.ResponsesInWait > 0)
                        break;
                }

                await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                    playerState.PlayerId,
                    JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
                    {
                        GameId = gameState.GameId,
                        PlayCards = playerState.TemporaryCards,
                        PlayerId = playerState.PlayerId,
                        Adjustments = playerState.TemporaryAdjustments,
                        RequestType = NinetyNineInGameRequestType.Play
                    })), ct);

                playerState.TemporaryCards.Clear();
                playerState.TemporaryAdjustments.Clear();
                playerState.ResponsesInWait = 0;

                break;
            }
            default:
            {
                await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                    playerState.PlayerId,
                    JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
                    {
                        GameId = gameState.GameId,
                        PlayCards = playerState.TemporaryCards,
                        PlayerId = playerState.PlayerId,
                        Adjustments = new[] { plusOrMinus },
                        RequestType = NinetyNineInGameRequestType.Play
                    })), ct);

                playerState.TemporaryCards.Clear();

                break;
            }
        }

        return Result.FromSuccess();
    }

    private async Task<Result> GiveUp(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        CancellationToken ct)
    {
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                RequestType = NinetyNineInGameRequestType.Burst
            })), ct);
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
}