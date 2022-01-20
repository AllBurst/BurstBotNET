using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.RedDotsPicking;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.Help;

#pragma warning disable CA2252
public class Help : CommandGroup
{
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly InteractionContext _context;

    public Help(
        InteractionContext context,
        IDiscordRestInteractionAPI interactionApi,
        State state)
    {
        _context = context;
        _interactionApi = interactionApi;
        _state = state;
    }

    [Command("help")]
    [Description("Show help and guide of each game.")]
    public async Task<Result> Handle(
        [Description("Show help and guide of each game.")]
        GameType? gameName = null
        )
    {
        var localization = _state.Localizations.GetLocalization();
        
        if (!gameName.HasValue)
        {
            var generalHelpResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
                    localization.Bot);
            return generalHelpResult.IsSuccess ? Result.FromSuccess() : Result.FromError(generalHelpResult);
        }

        return await DispatchHelp(gameName.Value);
    }

    public override string ToString() => "help";

    private async Task<Result> DispatchHelp(GameType gameType)
        => gameType switch
        {
            GameType.BlackJack => await BlackJackButtonEntity.ShowHelpMenu(_context, _state, _interactionApi),
            GameType.ChinesePoker => await ChinesePokerButtonEntity.ShowHelpMenu(_context, _state, _interactionApi),
            GameType.NinetyNine => Result.FromSuccess(),
            GameType.OldMaid => await OldMaidButtonEntity.ShowHelpMenu(_context, _state, _interactionApi),
            GameType.RedDotsPicking => await RedDotsButtonEntity.ShowHelpMenu(_context, _state, _interactionApi),
            GameType.ChaseThePig => await ChasePigButtonEntity.ShowHelpMenu(_context, _state, _interactionApi),
            _ => Result.FromSuccess()
        };
}