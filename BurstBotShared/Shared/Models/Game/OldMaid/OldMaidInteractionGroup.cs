using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.OldMaid;

public class OldMaidInteractionGroup : InteractionGroup, IHelpButtonEntity
{
    public const string DrawCustomId = "old_maid_draw";
    public const string HelpCustomId = "old_maid_help";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<OldMaidInteractionGroup> _logger;

    public OldMaidInteractionGroup(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<OldMaidInteractionGroup> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _state = state;
        _logger = logger;
        _interactionApi = interactionApi;
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().OldMaid;

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.CommandList["draw"]);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    [SelectMenu(DrawCustomId)]
    public async Task<IResult> Draw(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, DrawCustomId, values);

    [Button(HelpCustomId)]
    public async Task<IResult> Help() => await HandleInteractionAsync(_context.User, HelpCustomId);

    private async Task<Result> HandleInteractionAsync(IUser user, string customId, IEnumerable<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();
        
        if (!string.Equals(sanitizedCustomId, DrawCustomId)) return Result.FromSuccess();

        return await DrawCard(user, values, message!, ct);
    }
    
    private async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var _);
        if (!hasMessage) return Result.FromSuccess();

        var _ = Utilities.Utilities
            .GetGameState<OldMaidGameState, OldMaidPlayerState, OldMaidGameProgress>(user,
                _state.GameStates.OldMaidGameStates.Item1,
                _state.GameStates.OldMaidGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState?.TextChannel == null) return Result.FromSuccess();

        return await ShowHelpMenu(_context, _state, _interactionApi);
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

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            currentPlayer.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new OldMaidInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = currentPlayer.PlayerId,
                PlayerOrder = currentPlayer.Order,
                PickedCardId = pickedCardId,
                RequestType = OldMaidInGameRequestType.Draw
            })), ct);

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
}