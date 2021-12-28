#pragma warning disable CA2252
using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;

namespace BurstBotNET.Commands.NinetyNine;

using NinetyNineGame = IGame<NinetyNineGameState, RawNinetyNineGameState, NinetyNine, NinetyNineGameProgress, NinetyNineInGameRequestType>;
public partial class NinetyNine : NinetyNineGame
{
    public static Task<bool> HandleProgress(string messageContent, NinetyNineGameState gameState, State state, ILogger logger)
    {
        // TODO: Implement progress handling.
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, NinetyNineGameState state, Localizations localizations,
        DeckService deckService, ILogger logger)
    {
        // TODO: Implement ending result handling.
        throw new NotImplementedException();
    }
}