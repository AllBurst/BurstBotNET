using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerButtonEntity : IButtonInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;

    public ChinesePokerButtonEntity(InteractionContext context, State state, IDiscordRestChannelAPI channelApi)
    {
        _context = context;
        _state = state;
        _channelApi = channelApi;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(customId is "chinese_poker_confirm" or "chinese_poker_cancel");
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        if (!string.Equals(sanitizedCustomId, "chinese_poker_confirm") && !string.Equals(sanitizedCustomId, "chinese_poker_cancel"))
            return Result.FromSuccess();
        
        var gameState = Utilities.Utilities.GetChinesePokerGameState(user, _state, _context, out var playerState);
        
        if (playerState == null) return Result.FromSuccess();

        switch (sanitizedCustomId)
        {
            case "chinese_poker_confirm":
            {
                var dequeueResult = playerState.OutstandingMessages.TryDequeue(out var item);
                if (!dequeueResult)
                    break;

                var (cardMessage, _) = item;
                var originalEmbeds = cardMessage!.Embeds;
                var originalAttachments = cardMessage
                        .Attachments
                        .Select(OneOf<FileData, IPartialAttachment>.FromT1);
                var editResult = await _channelApi
                    .EditMessageAsync(cardMessage.ChannelID, cardMessage.ID,
                        embeds: originalEmbeds.ToImmutableArray(),
                        attachments: originalAttachments.ToImmutableArray(),
                        ct: ct);
                
                if (!editResult.IsSuccess) return Result.FromError(editResult);
                
                originalAttachments = message!.Attachments
                    .Select(OneOf<FileData, IPartialAttachment>.FromT1);
                editResult = await _channelApi
                    .EditMessageAsync(message.ChannelID, message.ID,
                        Constants.CheckMark,
                        attachments: originalAttachments.ToImmutableArray(),
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
                
                break;
            }
            case "chinese_poker_cancel":
            {
                var dequeueResult = playerState.OutstandingMessages.TryDequeue(out var _);
                if (!dequeueResult)
                    break;
                
                var editResult = await _channelApi
                    .EditMessageAsync(message!.ChannelID, message.ID,
                        Constants.CrossMark,
                        ct: ct);
                if (!editResult.IsSuccess) return Result.FromError(editResult);
                break;
            }
        }

        return Result.FromSuccess();
    }
}