using System.Collections.Immutable;
using System.Text.Json;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.OldMaid;

public class OldMaidDropDownEntity : ISelectMenuInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly State _state;
    
    public OldMaidDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        State state)
    {
        _context = context;
        _channelApi = channelApi;
        _state = state;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.SelectMenu
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(customId is "old_maid_draw");
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();
        
        if (!string.Equals(sanitizedCustomId, "old_maid_draw")) return Result.FromSuccess();

        return await DrawCard(user, values, message!, ct);
    }
    
    private async Task<Result> DrawCard(IUser user, IEnumerable<string> values, IMessage message, CancellationToken ct)
    {
        var gameState = Utilities.Utilities
            .GetGameState<OldMaidGameState, OldMaidPlayerState, OldMaidGameProgress>(user,
                _state.GameStates.OldMaidGameStates.Item1,
                _state.GameStates.OldMaidGameStates.Item2,
                _context,
                out var playerState);
        if (playerState == null) return Result.FromSuccess();

        var selection = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selection)) return Result.FromSuccess();

        var pickedCardId = int.Parse(selection);

        var currentPlayer = gameState
            .Players
            .First(p => p.Value.Order == gameState.CurrentPlayerOrder)
            .Value;

        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            currentPlayer.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new OldMaidInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = currentPlayer.PlayerId,
                PlayerOrder = currentPlayer.Order,
                PickedCardId = pickedCardId,
                RequestType = OldMaidInGameRequestType.Draw
            })), ct);

        var originalEmbeds = message.Embeds;
        var originalAttachments = message.Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);
        var originalComponents = message.Components.Disable();

        var editResult = await _channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                embeds: originalEmbeds.ToImmutableArray(),
                components: originalComponents,
                attachments: originalAttachments.ToImmutableArray(),
                ct: ct);

        return !editResult.IsSuccess ? Result.FromError(editResult) : Result.FromSuccess();
    }
}