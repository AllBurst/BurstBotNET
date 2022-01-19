using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerButtonEntity : IButtonInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<ChinesePokerButtonEntity> _logger;

    public ChinesePokerButtonEntity(
        InteractionContext context,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        ILogger<ChinesePokerButtonEntity> logger)
    {
        _context = context;
        _state = state;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _logger = logger;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(customId is "chinese_poker_confirm" or "chinese_poker_cancel" or "chinese_poker_help");
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
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
            "chinese_poker_confirm" => await ConfirmSelection(playerState, gameState, message!, ct),
            "chinese_poker_cancel" => await CancelSelection(playerState, message!, ct),
            "chinese_poker_help" => await ShowHelpMenu(_context, _state, _interactionApi),
            _ => Result.FromSuccess()
        };
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().ChinesePoker;

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("chinese_poker_help_selections", new[]
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

        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
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
                components: originalComponents,
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