using System.Collections.Immutable;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig;

public class ChasePigPlayerState : IState<ChasePigPlayerState, RawChasePigPlayerState, ChasePigGameProgress>, IPlayerState
{
    public string GameId { get; set; } = null!;
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = null!;
    public IChannel? TextChannel { get; set; }
    public string AvatarUrl { get; set; } = null!;
    public ImmutableArray<Card> Cards { get; set; }
    public int Scores { get; set; }
    public ImmutableArray<Card> CollectedCards { get; set; }
    public int Order { get; set; }
}