using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.RedDotsPicking;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;

namespace BurstBotNET.Commands.RedDotsPicking;

using RedDotsGame = IGame<RedDotsGameState, RawRedDotsGameState, RedDotsPicking, RedDotsPlayerState, RedDotsGameProgress, RedDotsInGameRequestType>;

public partial class RedDotsPicking : RedDotsGame
{
    public static Task<bool> HandleProgress(string messageContent, RedDotsGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgressChange(RawRedDotsGameState deserializedIncomingData, RedDotsGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, RedDotsGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
}