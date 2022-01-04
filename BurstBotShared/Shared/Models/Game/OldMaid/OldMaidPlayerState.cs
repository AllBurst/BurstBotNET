using System.Collections.Immutable;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.OldMaid;

public class OldMaidPlayerState : IState<OldMaidPlayerState, RawOldMaidPlayerState, OldMaidGameProgress>, IPlayerState
{
    public string GameId { get; set; } = null!;
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = null!;
    public IChannel? TextChannel { get; set; }
    public int Order { get; set; }
    public ImmutableArray<Card> Cards { get; set; } = new();
    public string AvatarUrl { get; set; } = null!;
}