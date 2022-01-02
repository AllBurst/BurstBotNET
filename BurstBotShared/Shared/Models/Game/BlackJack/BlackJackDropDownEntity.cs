using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackDropDownEntity : ISelectMenuInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    
    public BlackJackDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new CancellationToken())
    {
        return componentType is not ComponentType.SelectMenu
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(customId is "blackjack_help_selections");
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();
        
        if (!string.Equals(sanitizedCustomId, "blackjack_help_selections")) return Result.FromSuccess();

        return await ShowHelpTexts(user, values, ct);
    }
    
    private async Task<Result> ShowHelpTexts(IUser user, IEnumerable<string> values, CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().BlackJack;
        var _ = Utilities.Utilities
            .GetGameState<BlackJackGameState, BlackJackPlayerState, BlackJackGameProgress>(user,
                _state.GameStates.BlackJackGameStates.Item1,
                _state.GameStates.BlackJackGameStates.Item2,
                _context,
                out var playerState);
        if (playerState == null) return Result.FromSuccess();

        var selection = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selection)) return Result.FromSuccess();

        var getHelpResult = localization.CommandList.TryGetValue(selection, out var helpText);
        
        if (!getHelpResult) return Result.FromSuccess();

        var texts = new List<string>();
        if (helpText!.Length <= 2000)
            texts.Add(helpText);
        else
        {
            texts.Add(helpText[..2000]);
            texts.Add(helpText[2000..]);
        }

        foreach (var str in texts)
        {
            var result = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                    str, ct: ct);
            if (!result.IsSuccess)
                return Result.FromError(result);

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        
        return Result.FromSuccess();
    }
}