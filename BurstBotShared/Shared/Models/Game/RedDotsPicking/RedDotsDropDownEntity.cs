using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
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

public class RedDotsDropDownEntity : ISelectMenuInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;

    private readonly string[] _validCustomIds =
    {
        "red_dots_force_five_selection",
        "red_dots_user_selection",
        "red_dots_table_selection",
        "red_dots_give_up_selection",
        "red_dots_help_selection"
    };

    private readonly ILogger<RedDotsDropDownEntity> _logger;

    public RedDotsDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<RedDotsDropDownEntity> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.SelectMenu
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(_validCustomIds.Contains(customId));
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
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
                "red_dots_force_five_selection" => await PlayForceFive(message!, gameState, playerState, values, ct),
                "red_dots_user_selection" or "red_dots_table_selection" => await PlayCollectOrGiveUp(message!, gameState, playerState, values, customId, 2, ct),
                "red_dots_give_up_selection" => await PlayCollectOrGiveUp(message!, gameState, playerState, values, null, 1, ct),
                "red_dots_help_selection" => await ShowHelpText(values, ct),
                _ => Result.FromSuccess()
            };
        }

        if (sanitizedCustomId == "red_dots_help_selection")
            return await ShowHelpText(values, ct);
        
        return Result.FromSuccess();
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
                                    "red_dots_user_selection" => playerState
                                        .Cards
                                        .Where(c => Card.CanCombine(selectedCard, c))
                                        .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(),
                                            c.ToStringSimple(),
                                            new PartialEmoji(c.Suit.ToSnowflake()))),
                                    "red_dots_table_selection" => gameState
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