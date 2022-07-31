using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BurstBotShared.Services;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerInteractionGroup : InteractionGroup, IHelpInteraction
{
    public const string ChinesePokerCards = "chinese_poker_cards";
    public const string NaturalCards = "naturals";
    public const string HelpSelectionCustomId = "chinese_poker_help_selections";
    public const string ConfirmCustomId = "chinese_poker_confirm";
    public const string CancelCustomId = "chinese_poker_cancel";
    public const string HelpCustomId = "chinese_poker_help";
    
    private static readonly Regex CardRegex = new(@"([shdc])([0-9ajqk]+)");
    private static readonly string[] AvailableRanks;
    private static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<ChinesePokerInteractionGroup> _logger;

    static ChinesePokerInteractionGroup()
    {
        AvailableRanks = Enumerable
            .Range(2, 9)
            .Select(n => n.ToString())
            .Concat(new[] { "a", "j", "q", "k" })
            .ToArray();
    }
    
    public ChinesePokerInteractionGroup(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<ChinesePokerInteractionGroup> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().ChinesePoker;

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent(CustomIDHelpers.CreateSelectMenuID(HelpSelectionCustomId), new[]
                {
                    new SelectOption(localization.FrontHand, "Front Hand", localization.FrontHand, default, false),
                    new SelectOption(localization.MiddleHand, "Middle Hand", localization.MiddleHand, default, false),
                    new SelectOption(localization.BackHand, "Back Hand", localization.BackHand, default, false)
                }, localization.ShowHelp, 0, 1, false)
            })
        };

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.About,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    [SelectMenu(ChinesePokerCards)]
    public async Task<IResult> Cards(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, ChinesePokerCards, values);
    
    [SelectMenu(NaturalCards)]
    public async Task<IResult> Naturals(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, NaturalCards, values);

    [SelectMenu(HelpSelectionCustomId)]
    public async Task<IResult> HelpSelections(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, HelpSelectionCustomId, values);

    [Button(ConfirmCustomId)]
    public async Task<IResult> Confirm() => await HandleInteractionAsync(_context.User, ConfirmCustomId);
    
    [Button(CancelCustomId)]
    public async Task<IResult> Cancel() => await HandleInteractionAsync(_context.User, CancelCustomId);

    [Button(HelpCustomId)]
    public async Task<IResult> Help() => await HandleInteractionAsync(_context.User, HelpCustomId);

    private async Task<IResult> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        if (_state.GameStates.ChinesePokerGameStates.Item2.Contains(_context.ChannelID))
        {
            var _ = Utilities.Utilities
                .GetGameState<ChinesePokerGameState, ChinesePokerPlayerState, ChinesePokerGameProgress>(user,
                    _state.GameStates.ChinesePokerGameStates.Item1,
                    _state.GameStates.ChinesePokerGameStates.Item2,
                    _context,
                    out var playerState);
            if (playerState == null) return Result.FromSuccess();
        }
        
        var sanitizedCustomId = customId.Trim();

        return sanitizedCustomId switch
        {
            ChinesePokerCards => await HandleSelectCards(message!, user, values, ct),
            NaturalCards => await HandleSelectNatural(message!, user, values, ct),
            HelpSelectionCustomId => await ShowHelpTexts(values, ct),
            _ => Result.FromSuccess()
        };
    }
    
    private async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        var gameState = Utilities.Utilities
            .GetGameState<ChinesePokerGameState, ChinesePokerPlayerState, ChinesePokerGameProgress>(user,
                _state.GameStates.ChinesePokerGameStates.Item1,
                _state.GameStates.ChinesePokerGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState == null) return Result.FromSuccess();

        return sanitizedCustomId switch
        {
            ConfirmCustomId => await ConfirmSelection(playerState, gameState, message!, ct),
            CancelCustomId => await CancelSelection(playerState, message!, ct),
            HelpCustomId => await ShowHelpMenu(_context, _state, _interactionApi),
            _ => Result.FromSuccess()
        };
    }

    private async Task<IResult> HandleSelectCards(
        IMessage message,
        IUser user,
        IReadOnlyList<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;
        var gameState = Utilities.Utilities
            .GetGameState<ChinesePokerGameState, ChinesePokerPlayerState, ChinesePokerGameProgress>(user,
                _state.GameStates.ChinesePokerGameStates.Item1,
                _state.GameStates.ChinesePokerGameStates.Item2,
                _context,
                out var playerState);
        if (playerState == null) return Result.FromSuccess();
        
        var handName = gameState.Progress switch
        {
            ChinesePokerGameProgress.FrontHand => localization.FrontHand.ToLowerInvariant(),
            ChinesePokerGameProgress.MiddleHand => localization.MiddleHand.ToLowerInvariant(),
            ChinesePokerGameProgress.BackHand => localization.BackHand.ToLowerInvariant(),
            _ => ""
        };
        
        if (values.Any(s => !CardRegex.IsMatch(s)))
        {
            var invalidCard = await _interactionApi
                .CreateFollowupMessageAsync(
                    _context.ApplicationID,
                    _context.Token,
                    localization.InvalidCard, ct: ct);
            return Result.FromError(invalidCard);
        }
        
        var cardMatches = values
            .Select(s => CardRegex.Match(s))
            .ToImmutableArray();
        
        if (cardMatches.Any(m => !AvailableRanks.Contains(m.Groups[2].Value)))
        {
            var invalidCard = await _interactionApi
                .CreateFollowupMessageAsync(
                    _context.ApplicationID,
                    _context.Token,
                    localization.InvalidCard, ct: ct);
            return Result.FromError(invalidCard);
        }
        
        var cards = cardMatches
            .Select(m => Card.Create(m.Groups[1].Value, m.Groups[2].Value))
            .ToImmutableArray()
            .Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()));
        
        if (cards.Any(c => !playerState.Cards.Contains(c)))
        {
            var invalidCard = await _interactionApi
                .CreateFollowupMessageAsync(
                    _context.ApplicationID,
                    _context.Token,
                    localization.InvalidCard, ct: ct);
            return Result.FromError(invalidCard);
        }
        
        var renderedCards = SkiaService.RenderDeck(_state.DeckService, cards);
        var embedBuildResult = new EmbedBuilder()
            .WithColour(BurstColor.Burst.ToColor())
            .WithDescription(
                $"{localization.Cards}\n{string.Join('\n', cards)}\n{localization.ConfirmCards.Replace("{hand}", handName)}")
            .WithThumbnailUrl(Constants.BurstLogo)
            .WithTitle(TextInfo.ToTitleCase(handName))
            .Build();
        var embed = 
                embedBuildResult.Entity with
            {
                Author = new EmbedAuthor(playerState.PlayerName, IconUrl: playerState.AvatarUrl),
                Footer = new EmbedFooter(localization.ConfirmCardsFooter.Replace("{hand}", handName)),
                Image = new EmbedImage(Constants.AttachmentUri)
            };

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new ButtonComponent(ButtonComponentStyle.Success, localization.Confirm,
                    new PartialEmoji(Name: Constants.CheckMark), CustomIDHelpers.CreateButtonID(ConfirmCustomId)),
                new ButtonComponent(ButtonComponentStyle.Danger, localization.Cancel,
                    new PartialEmoji(Name: Constants.CrossMark), CustomIDHelpers.CreateButtonID(CancelCustomId)),
            })
        };

        await using var renderedCardsCopy = new MemoryStream((int)renderedCards.Length);
        await renderedCards.CopyToAsync(renderedCardsCopy, ct);
        renderedCards.Seek(0, SeekOrigin.Begin);
        renderedCardsCopy.Seek(0, SeekOrigin.Begin);

        var confirmMessageResult = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                embeds: new[] { embed },
                components: components,
                attachments: new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedCardsCopy)),
                },
                ct: ct);
        
        if (!confirmMessageResult.IsSuccess) return Result.FromError(confirmMessageResult);

        var currentProgress = gameState.Progress;
        playerState.DeckImages[currentProgress] = renderedCards;
        playerState.PlayedCards[currentProgress] = new ChinesePokerCombination
        {
            Cards = cards.ToList(),
            CombinationType = ChinesePokerCombinationType.None
        };
        playerState.OutstandingMessages.Enqueue(message);
        return Result.FromSuccess();
    }

    private async Task<IResult> HandleSelectNatural(IMessage message,
        IUser user,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;
        var _ = Utilities.Utilities
            .GetGameState<ChinesePokerGameState, ChinesePokerPlayerState, ChinesePokerGameProgress>(user,
                _state.GameStates.ChinesePokerGameStates.Item1,
                _state.GameStates.ChinesePokerGameStates.Item2,
                _context,
                out var playerState);
        if (playerState == null) return Result.FromSuccess();

        var value = values.FirstOrDefault();
        var parseResult = Enum.TryParse<ChinesePokerNatural>(value ?? "", out var natural);
        if (!parseResult)
            return Result.FromSuccess();

        playerState.Naturals = natural;
        var result = await _interactionApi
            .EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
                localization.NaturalDeclared, ct: ct);

        if (!result.IsSuccess) return Result.FromError(result);

        var deleteResult = await _channelApi
            .DeleteMessageAsync(message.ChannelID, message.ID, ct: ct);
        
        return !deleteResult.IsSuccess ? Result.FromError(deleteResult.Error) : Result.FromSuccess();
    }

    private async Task<IResult> ShowHelpTexts(IEnumerable<string> values, CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;

        var selection = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selection)) return Result.FromSuccess();

        var helpText = selection switch
        {
            "Front Hand" => localization.CommandList["fronthand"],
            "Middle Hand" => localization.CommandList["middlehand"]
                .Replace("{hand}", localization.MiddleHand.ToLowerInvariant()),
            "Back Hand" => localization.CommandList["backhand"]
                .Replace("{hand}", localization.BackHand.ToLowerInvariant()),
            _ => ""
        };

        var texts = new List<string>();
        if (helpText.Length <= 2000)
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

    private async Task<Result> ConfirmSelection(
        ChinesePokerPlayerState playerState,
        ChinesePokerGameState gameState,
        IMessage message,
        CancellationToken ct)
    {
        var dequeueResult = playerState.OutstandingMessages.TryDequeue(out var cardMessage);
        if (!dequeueResult)
            return Result.FromSuccess();

        await Utilities.Utilities.DisableComponents(cardMessage!, true, _channelApi, _logger, ct);
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChinesePokerInGameRequest
            {
                RequestType = gameState.Progress switch
                {
                    ChinesePokerGameProgress.FrontHand => ChinesePokerInGameRequestType.FrontHand,
                    ChinesePokerGameProgress.MiddleHand => ChinesePokerInGameRequestType.MiddleHand,
                    ChinesePokerGameProgress.BackHand => ChinesePokerInGameRequestType.BackHand,
                    _ => ChinesePokerInGameRequestType.Close
                },
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayCard = playerState.PlayedCards[gameState.Progress].Cards.ToImmutableArray(),
                DeclaredNatural = playerState.Naturals
            })), ct);
        
        return Result.FromSuccess();
    }

    private async Task<Result> CancelSelection(
        ChinesePokerPlayerState playerState,
        IMessage message,
        CancellationToken ct)
    {
        var dequeueResult = playerState.OutstandingMessages.TryDequeue(out var cardMessage);
        if (!dequeueResult)
            return Result.FromSuccess();

        var originalEmbeds = cardMessage!.Embeds;
        var originalAttachments = cardMessage
            .Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);
        var originalComponents = cardMessage.Components;

        var editResult = await _channelApi
            .EditMessageAsync(cardMessage.ChannelID, cardMessage.ID,
                embeds: originalEmbeds.ToImmutableArray(),
                components: originalComponents!,
                attachments: originalAttachments.ToImmutableArray(),
                ct: ct);

        if (!editResult.IsSuccess)
            _logger.LogError("Failed to edit original message: {Reason}, inner: {Inner}",
                editResult.Error.Message, editResult.Inner);
                
        editResult = await _channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                Constants.CrossMark,
                attachments: Array.Empty<OneOf<FileData, IPartialAttachment>>(),
                components: Array.Empty<IMessageComponent>(),
                embeds: Array.Empty<IEmbed>(),
                ct: ct);
        return !editResult.IsSuccess ? Result.FromError(editResult) : Result.FromSuccess();
    }
}