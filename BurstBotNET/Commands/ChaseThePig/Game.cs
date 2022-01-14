using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;

namespace BurstBotNET.Commands.ChaseThePig;

using ChasePigGame = IGame<ChasePigGameState, RawChasePigGameState, ChaseThePig, ChasePigPlayerState, ChasePigGameProgress, ChasePigInGameRequestType>;

public partial class ChaseThePig : ChasePigGame
{
    public static Task<bool> HandleProgress(string messageContent, ChasePigGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgressChange(RawChasePigGameState deserializedIncomingData, ChasePigGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, ChasePigGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
}