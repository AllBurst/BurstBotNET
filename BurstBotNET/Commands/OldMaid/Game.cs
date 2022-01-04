using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotNET.Commands.OldMaid;

using OldMaidGame = IGame<OldMaidGameState, RawOldMaidGameState, OldMaid, OldMaidPlayerState, OldMaidGameProgress, OldMaidInGameRequestType>;
public partial class OldMaid : OldMaidGame
{
    public static Task AddPlayerState(string gameId, Snowflake guild, OldMaidPlayerState playerState, GameStates gameStates)
    {
        throw new NotImplementedException();
    }

    public static Task StartListening(string gameId, State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgress(string messageContent, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgressChange(RawOldMaidGameState deserializedIncomingData, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, OldMaidGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
}