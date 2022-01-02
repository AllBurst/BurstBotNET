using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
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

    public BlackJackButtonEntity(
        InteractionContext context,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi)
    {
        _context = context;
        _state = state;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
    }

    public static async Task SendRaiseData(
        BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        int raiseBet)
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
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        var isValidButton = customId is "draw" or "stand" or "call" or "raise" or "fold" or "allin";
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

        switch (sanitizedCustomId)
        {
            case "draw":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Draw);
                break;
            case "stand":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Stand);
                break;
            case "fold":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Fold);
                break;
            case "call":
                await SendGenericData(gameState, playerState, BlackJackInGameRequestType.Call);
                break;
            case "raise":
                return await HandleRaise(playerState);
            case "allin":
            {
                var remainingTips = playerState.OwnTips - playerState.BetTips - gameState.HighestBet;
                await SendRaiseData(gameState, playerState, (int)remainingTips);
                break;
            }
        }

        return Result.FromSuccess();
    }
    
    private static async Task SendGenericData(BlackJackGameState gameState,
        BlackJackPlayerState playerState,
        BlackJackInGameRequestType requestType)
    {
        var sendData = new Tuple<ulong, byte[]>(playerState.PlayerId, JsonSerializer.SerializeToUtf8Bytes(
            new BlackJackInGameRequest
            {
                RequestType = requestType,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId
            }));
        await gameState.Channel!.Writer.WriteAsync(sendData);
    }

    private async Task<Result> HandleRaise(BlackJackPlayerState playerState)
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;
        playerState.IsRaising = true;

        var result = await _channelApi
            .CreateMessageAsync(playerState.TextChannel!.ID, localization.RaisePrompt);

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

        var originalEmbeds = cardMessage!.Embeds;
        var originalAttachments = cardMessage
            .Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);
        var editResult = await _channelApi
            .EditMessageAsync(cardMessage.ChannelID, cardMessage.ID,
                embeds: originalEmbeds.ToImmutableArray(),
                attachments: originalAttachments.ToImmutableArray(),
                components: Array.Empty<IMessageComponent>(),
                ct: ct);
                
        if (!editResult.IsSuccess) return Result.FromError(editResult);

        originalEmbeds = message.Embeds;
        originalAttachments = message.Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);
        editResult = await _channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                Constants.CheckMark,
                originalEmbeds.ToImmutableArray(),
                attachments: originalAttachments.ToImmutableArray(),
                components: Array.Empty<IMessageComponent>(),
                ct: ct);
                
        if (!editResult.IsSuccess) return Result.FromError(editResult);

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
        var dequeueResult = playerState.OutstandingMessages.TryDequeue(out _);
        if (!dequeueResult)
            return Result.FromSuccess();
                
        var editResult = await _channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                Constants.CrossMark,
                attachments: Array.Empty<OneOf<FileData, IPartialAttachment>>(),
                components: Array.Empty<IMessageComponent>(),
                embeds: Array.Empty<IEmbed>(),
                ct: ct);
        return !editResult.IsSuccess ? Result.FromError(editResult) : Result.FromSuccess();
    }

    private async Task<Result> ShowHelpMenu()
    {
        var localization = _state.Localizations.GetLocalization().ChinesePoker;

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("help_selections", new[]
                {
                    new SelectOption(localization.FrontHand, "Front Hand", localization.FrontHand, default, false),
                    new SelectOption(localization.MiddleHand, "Middle Hand", localization.MiddleHand, default, false),
                    new SelectOption(localization.BackHand, "Back Hand", localization.BackHand, default, false)
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