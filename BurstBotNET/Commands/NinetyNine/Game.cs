#pragma warning disable CA2252
using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;

namespace BurstBotNET.Commands.NinetyNine;

using NinetyNineGame = IGame<NinetyNineGameState, RawNinetyNineGameState, NinetyNine, NinetyNineGameProgress, NinetyNineInGameRequestType>;
public partial class NinetyNine : NinetyNineGame
{
    public static Task<bool> HandleProgress(string messageContent, NinetyNineGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, NinetyNineGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
}