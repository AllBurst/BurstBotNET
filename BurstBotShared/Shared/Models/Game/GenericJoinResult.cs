using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game;

public record GenericJoinResult
{
    public GenericJoinStatus JoinStatus { get; init; } = null!;
    public string Reply { get; init; } = "";
    public IGuildMember InvokingMember { get; init; } = null!;
    public IUser BotUser { get; init; } = null!;
    public ImmutableArray<Snowflake> MentionedPlayers { get; init; } = ImmutableArray<Snowflake>.Empty;
    public RawTip InvokerTip { get; init; } = null!;
};