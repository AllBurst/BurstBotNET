using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.RedDotsPicking.Serializables;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Rest.Core;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking;

public class RedDotsInteractionGroup : InteractionGroup, IHelpButtonEntity
{
    public const string ForceFiveCustomId = "red_dots_force_five_selection";
    public const string UserSelectionCustomId = "red_dots_user_selection";
    public const string TableSelectionCustomId = "red_dots_table_selection";
    public const string GiveUpSelectionCustomId = "red_dots_give_up_selection";
    public const string HelpSelectionCustomId = "red_dots_help_selection";
    public const string HelpCustomId = "red_dots_help";
    
    private static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;

    private readonly ILogger<RedDotsInteractionGroup> _logger;

    public RedDotsInteractionGroup(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<RedDotsInteractionGroup> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().RedDotsPicking;

        var options = localization
            .CommandList
            .Keys
            .Select(k =>
            {
                var (desc, emoji) = GetHelpMenuItemDescription(k, localization);
                return (k, desc, emoji);
            })
            .Select(item => new SelectOption(TextInfo.ToTitleCase(item.k), item.k, item.desc, item.emoji));

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent(CustomIDHelpers.CreateSelectMenuID(HelpSelectionCustomId), options.ToImmutableArray(), localization.ShowHelp, 1,
                    1)
            })
        };

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.About,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    [SelectMenu(ForceFiveCustomId)]
    public async Task<IResult> ForceFive(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, ForceFiveCustomId, values);
    
    [SelectMenu(UserSelectionCustomId)]
    public async Task<IResult> SelectUserCard(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, UserSelectionCustomId, values);
    
    [SelectMenu(TableSelectionCustomId)]
    public async Task<IResult> SelectTableCard(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, TableSelectionCustomId, values);
    
    [SelectMenu(GiveUpSelectionCustomId)]
    public async Task<IResult> SelectGiveUpCard(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, GiveUpSelectionCustomId, values);
    
    [SelectMenu(HelpSelectionCustomId)]
    public async Task<IResult> SelectHelp(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, HelpSelectionCustomId, values);
    
    [SelectMenu(HelpCustomId)]
    public async Task<IResult> Help() =>
        await HandleInteractionAsync(_context.User, HelpCustomId);

    private async Task<Result> HandleInteractionAsync(IUser user, string customId, IEnumerable<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        if (_state.GameStates.RedDotsGameStates.Item2.Contains(_context.ChannelID))
        {
            var gameState = Utilities.Utilities
                .GetGameState<RedDotsGameState, RedDotsPlayerState, RedDotsGameProgress>(user,
                    _state.GameStates.RedDotsGameStates.Item1,
                    _state.GameStates.RedDotsGameStates.Item2,
                    _context,
                    out var playerState);
            if (playerState == null) return Result.FromSuccess();

            return sanitizedCustomId switch
            {
                ForceFiveCustomId => await PlayForceFive(message!, gameState, playerState, values, ct),
                UserSelectionCustomId or TableSelectionCustomId => await PlayCollectOrGiveUp(message!, gameState, playerState, values, customId, 2, ct),
                GiveUpSelectionCustomId => await PlayCollectOrGiveUp(message!, gameState, playerState, values, null, 1, ct),
                HelpSelectionCustomId => await ShowHelpText(values, ct),
                _ => Result.FromSuccess()
            };
        }

        if (sanitizedCustomId == HelpSelectionCustomId)
            return await ShowHelpText(values, ct);
        
        return Result.FromSuccess();
    }
    
    private async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var _);
        if (!hasMessage) return Result.FromSuccess();

        var _ = Utilities.Utilities
            .GetGameState<RedDotsGameState, RedDotsPlayerState, RedDotsGameProgress>(user,
                _state.GameStates.RedDotsGameStates.Item1,
                _state.GameStates.RedDotsGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState?.TextChannel == null) return Result.FromSuccess();

        return await ShowHelpMenu(_context, _state, _interactionApi);
    }

    private static bool Validate(RedDotsPlayerState playerState, int count) => playerState.PlayedCards.Count == count;

    private static async Task SendToGameStateChannel(RedDotsGameState gameState, RedDotsPlayerState playerState, CancellationToken ct)
    {
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new RedDotsInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayerOrder = playerState.Order,
                PlayedCards = playerState.PlayedCards,
                RequestType = RedDotsInGameRequestType.Play
            })), ct);

        playerState.PlayedCards.Clear();
    }
    
    private static List<IMessageComponent> BuildNewMenu(
        Optional<IReadOnlyList<IMessageComponent>> components,
        Card selectedCard,
        RedDotsGameState gameState,
        RedDotsPlayerState playerState,
        IEnumerable<string>? customIds = null)
    {
        if (!components.HasValue) return new List<IMessageComponent>();

        var idsToRemove = customIds?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            
        var newComponents = new List<IMessageComponent>(components.Value.Count);
        foreach (var component in components.Value)
        {
            if (component is not ActionRowComponent actionRow) continue;

            var newActionRow = new List<IMessageComponent>(actionRow.Components.Count);

            foreach (var inner in actionRow.Components)
            {
                switch (inner)
                {
                    case ButtonComponent button:
                    {
                        if (!idsToRemove.IsEmpty && idsToRemove.Contains(button.CustomID.Value))
                            newActionRow.Add(button with { IsDisabled = true });
                        else
                            newActionRow.Add(button);
                        break;
                    }
                    case SelectMenuComponent menu:
                    {
                        if (!idsToRemove.IsEmpty)
                        {
                            if (idsToRemove.Contains(menu.CustomID))
                                newActionRow.Add(menu with { IsDisabled = true });
                            else
                            {
                                var availableTableCards = menu.CustomID switch
                                {
                                    UserSelectionCustomId => playerState
                                        .Cards
                                        .Where(c => Card.CanCombine(selectedCard, c))
                                        .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(),
                                            c.ToStringSimple(),
                                            new PartialEmoji(c.Suit.ToSnowflake()))),
                                    TableSelectionCustomId => gameState
                                        .CardsOnTable
                                        .Where(c => Card.CanCombine(selectedCard, c))
                                        .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(),
                                            c.ToStringSimple(),
                                            new PartialEmoji(c.Suit.ToSnowflake()))),
                                    _ => Enumerable.Empty<SelectOption>()
                                };

                                var newMenu = menu with
                                {
                                    Options = availableTableCards.ToImmutableArray()
                                };

                                newActionRow.Add(newMenu);
                            }
                        }
                        else
                            newActionRow.Add(menu with { IsDisabled = true });
                        break;
                    }
                }
            }

            newComponents.Add(actionRow with { Components = newActionRow });
        }

        return newComponents;
    }
    
    private static (string, PartialEmoji) GetHelpMenuItemDescription(string key, RedDotsLocalization localization)
        => key switch
        {
            "general" => (localization.ShowGeneral, new PartialEmoji(Name: "ℹ️")),
            "scoring" => (localization.ShowScoring, new PartialEmoji(Name: "💯")),
            "flows" => (localization.ShowFlows, new PartialEmoji(Name: "➡️")),
            _ => ("", new PartialEmoji(Name: "❓"))
        };

    private async Task<Result> PlayCollectOrGiveUp(
        IMessage message,
        RedDotsGameState gameState,
        RedDotsPlayerState playerState,
        IEnumerable<string> values,
        string? customId,
        int validateCount,
        CancellationToken ct)
    {
        var extractedCard = Utilities.Utilities.ExtractCard(values);
        playerState.PlayedCards.Add(extractedCard);
        var validateResult = Validate(playerState, validateCount);

        var customIdsToRemove =
            customId != null ? new[] { customId }.ToImmutableArray() : ImmutableArray<string>.Empty;
        
        var originalEmbeds = message.Embeds;
        var originalComponents = BuildNewMenu(
            message.Components,
            extractedCard,
            gameState,
            playerState,
            customIdsToRemove);
        var originalAttachments = message
            .Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);

        var editResult = await _channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                embeds: originalEmbeds.ToImmutableArray(),
                components: originalComponents,
                //attachments: originalAttachments.ToImmutableArray(),
                ct: ct);
        if (!editResult.IsSuccess) return Result.FromError(editResult);

        if (validateResult)
            await SendToGameStateChannel(gameState, playerState, ct);
        
        return Result.FromSuccess();
    }

    private async Task<Result> PlayForceFive(
        IMessage message,
        RedDotsGameState gameState,
        RedDotsPlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        playerState.PlayedCards.Add(Utilities.Utilities.ExtractCard(values));

        var redFive = gameState
            .CardsOnTable
            .FirstOrDefault(c => c.Suit is Suit.Heart or Suit.Diamond && c.Number == 5);
        playerState.PlayedCards.Add(redFive!);

        var validateResult = Validate(playerState, 2);
        if (!validateResult) return Result.FromSuccess();

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        await SendToGameStateChannel(gameState, playerState, ct);
        
        return Result.FromSuccess();
    }

    private async Task<Result> ShowHelpText(
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().RedDotsPicking;
        var content = localization.CommandList[values.FirstOrDefault()!];

        var texts = new List<string>();
        if (content.Length <= 2000)
            texts.Add(content);
        else
        {
            texts.Add(content[..2000]);
            texts.Add(content[2000..]);
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
}