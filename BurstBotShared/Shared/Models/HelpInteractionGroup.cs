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

public class HelpInteractionGroup : InteractionGroup
{
    public const string VariationSelection = "ninety_nine_variation_selection";

    private readonly InteractionContext _context;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<HelpInteractionGroup> _logger;
    private readonly State _state;

    public HelpInteractionGroup(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi, ILogger<HelpInteractionGroup> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _logger = logger;
    }

    [SelectMenu(VariationSelection)]
    public async Task<IResult> SelectionVariation(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, VariationSelection, values);

    private async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out _);
        if (!hasMessage) return Result.FromSuccess();

        var selection = values[0].Trim();
        var variation = Enum.Parse<NinetyNineVariation>(selection);
        return await NinetyNineInteractionGroup.ShowHelpMenu(variation, _context, _state, _interactionApi);
    }
}