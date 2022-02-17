using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models;

public class HelpDropDownEntity : ISelectMenuInteractiveEntity
{
    private readonly string[] _validCustomIds =
    {
        "ninety_nine_variation_selection"
    };

    private readonly InteractionContext _context;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<HelpDropDownEntity> _logger;
    private readonly State _state;

    public HelpDropDownEntity(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi, ILogger<HelpDropDownEntity> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
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
        var hasMessage = _context.Message.IsDefined(out _);
        if (!hasMessage) return Result.FromSuccess();

        var selection = values[0].Trim();
        var variation = Enum.Parse<NinetyNineVariation>(selection);
        return await NinetyNineButtonEntity.ShowHelpMenu(variation, _context, _state, _interactionApi);
    }
}