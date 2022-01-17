using System.Text.Json;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OldMaidDropDownEntity> _logger;

    public OldMaidDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        State state,
        ILogger<OldMaidDropDownEntity> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _state = state;
        _logger = logger;
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

        await Utilities.Utilities.DisableComponents(message, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
}