using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.OldMaid;

public class OldMaidButtonEntity : IButtonInteractiveEntity, IHelpButtonEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;

    public OldMaidButtonEntity(
        InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
    }

    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        var isValidButton = customId is "old_maid_help";
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(isValidButton);
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
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
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().OldMaid;

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.CommandList["draw"]);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}