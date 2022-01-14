using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Localization.RedDotsPicking.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking;

public class RedDotsButtonEntity : IButtonInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly TextInfo _textInfo = CultureInfo.InvariantCulture.TextInfo;

    public RedDotsButtonEntity(
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
        var isValidButton = customId is "red_dots_help";
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(isValidButton);
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var _);
        if (!hasMessage) return Result.FromSuccess();

        var _ = Utilities.Utilities
            .GetGameState<RedDotsGameState, RedDotsPlayerState, RedDotsGameProgress>(user,
                _state.GameStates.RedDotsGameStates.Item1,
                _state.GameStates.RedDotsGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState?.TextChannel == null) return Result.FromSuccess();

        return await ShowHelpMenu();
    }

    private static (string, PartialEmoji) GetHelpMenuItemDescription(string key, RedDotsLocalization localization)
        => key switch
        {
            "general" => (localization.ShowGeneral, new PartialEmoji(Name: "ℹ️")),
            "scoring" => (localization.ShowScoring, new PartialEmoji(Name: "💯")),
            "flows" => (localization.ShowFlows, new PartialEmoji(Name: "➡️")),
            _ => ("", new PartialEmoji(Name: "❓"))
        };

    private async Task<Result> ShowHelpMenu()
    {
        var localization = _state.Localizations.GetLocalization().RedDotsPicking;

        var options = localization
            .CommandList
            .Keys
            .Select(k =>
            {
                var (desc, emoji) = GetHelpMenuItemDescription(k, localization);
                return (k, desc, emoji);
            })
            .Select(item => new SelectOption(_textInfo.ToTitleCase(item.k), item.k, item.desc, item.emoji));

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("red_dots_help_selection", options.ToImmutableArray(), localization.ShowHelp, 1,
                    1)
            })
        };

        var result = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                localization.ShowHelp,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}