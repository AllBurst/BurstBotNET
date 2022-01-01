using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using BurstBotShared.Services;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerDropDownEntity : ISelectMenuInteractiveEntity
{
    private static readonly Regex CardRegex = new(@"([shdc])([0-9ajqk]+)");
    private static readonly string[] AvailableRanks;
    private static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;

    static ChinesePokerDropDownEntity()
    {
        AvailableRanks = Enumerable
            .Range(2, 9)
            .Select(n => n.ToString())
            .Concat(new[] { "a", "j", "q", "k" })
            .ToArray();
    }
    
    public ChinesePokerDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.SelectMenu
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(customId is "chinese_poker_cards");
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        switch (sanitizedCustomId)
        {
            case "chinese_poker_cards":
            {
                var result = await HandleSelectCards(message!, user, values, ct);
                if (!result.IsSuccess) return Result.FromError(result.Error);
                break;
            }
            case "naturals":
            {
                var result = await HandleSelectNatural(message!, user, values, ct);
                if (!result.IsSuccess) return Result.FromError(result.Error);
                break;
            }
        }
        
        return Result.FromSuccess();
    }

    private async Task<Result> HandleSelectCards(
        IMessage message,
        IUser user,
        IReadOnlyList<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;
        var gameState = Utilities.Utilities.GetChinesePokerGameState(user, _state, _context, out var playerState);
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
            /*var invalidCard = await _channelApi
                .CreateMessageAsync(_context.ChannelID, localization.InvalidCard, ct: ct);*/
            var invalidCard = await _interactionApi
                .EditOriginalInteractionResponseAsync(
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
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    localization.InvalidCard, ct: ct);
            return Result.FromError(invalidCard);
        }
        
        var cards = cardMatches
            .Select(m => Card.CreateCard(m.Groups[1].Value, m.Groups[2].Value))
            .ToImmutableArray()
            .Sort((a, b) => a.Number.CompareTo(b.Number));
        
        if (cards.Any(c => !playerState.Cards.Contains(c)))
        {
            var invalidCard = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    localization.InvalidCard, ct: ct);
            return Result.FromError(invalidCard);
        }
        
        var renderedCards = SkiaService.RenderDeck(_state.DeckService, cards);
        var embed = new EmbedBuilder()
            .WithAuthor(playerState.PlayerName, iconUrl: playerState.AvatarUrl)
            .WithColour(BurstColor.Burst.ToColor())
            .WithDescription(
                $"{localization.Cards}\n{string.Join('\n', cards)}\n{localization.ConfirmCards.Replace("{hand}", handName)}")
            .WithFooter(localization.ConfirmCardsFooter.Replace("{hand}", handName))
            .WithThumbnailUrl(Constants.BurstLogo)
            .WithTitle(TextInfo.ToTitleCase(handName))
            .WithImageUrl(Constants.AttachmentUri);

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new ButtonComponent(ButtonComponentStyle.Success, localization.Confirm,
                    new PartialEmoji(Name: Constants.CheckMark), "chinese_poker_confirm"),
                new ButtonComponent(ButtonComponentStyle.Danger, localization.Confirm,
                    new PartialEmoji(Name: Constants.CheckMark), "chinese_poker_cancel"),
            })
        };

        var confirmMessageResult = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                embeds: new[] { embed.Build().Entity },
                components: components,
                attachments: new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedCards)),
                },
                ct: ct);

        renderedCards.Seek(0, SeekOrigin.Begin);
        if (!confirmMessageResult.IsSuccess) return Result.FromError(confirmMessageResult);

        var currentProgress = gameState.Progress;
        playerState.DeckImages[currentProgress] = renderedCards;
        playerState.PlayedCards[currentProgress] = new ChinesePokerCombination
        {
            Cards = cards.ToList(),
            CombinationType = ChinesePokerCombinationType.None
        };
        playerState.OutstandingMessages.Enqueue((message, confirmMessageResult.Entity));
        return Result.FromSuccess();
    }

    private async Task<Result> HandleSelectNatural(IMessage message,
        IUser user,
        IReadOnlyList<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;
        var _ = Utilities.Utilities.GetChinesePokerGameState(user, _state, _context, out var playerState);
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
}