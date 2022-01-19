using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;

namespace BurstBotShared.Shared.Models.Game;

public record GenericStartGameData
{
    public InteractionContext Context { get; init; } = null!;
    public string Reply { get; init; } = null!;
    public IGuildMember InvokingMember { get; init; } = null!;
    public IUser BotUser { get; init; } = null!;
    public GenericJoinStatus JoinStatus { get; init; } = null!;
    public string GameName { get; init; } = null!;
    public string ConfirmationEndpoint { get; init; } = null!;
    public IEnumerable<ulong> PlayerIds { get; init; } = null!;
    public State State { get; init; } = null!;
    public int MinPlayerCount { get; init; }
    public IDiscordRestInteractionAPI InteractionApi { get; init; } = null!;
    public IDiscordRestChannelAPI ChannelApi { get; init; } = null!;
    public IDiscordRestGuildAPI GuildApi { get; init; } = null!;
    public ILogger Logger { get; init; } = null!;
};