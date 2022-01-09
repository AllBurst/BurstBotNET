using System.Collections.Immutable;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking;

public class RedDotsPlayerState : IState<RedDotsPlayerState, RawRedDotsPlayerState, RedDotsGameProgress>, IPlayerState
{
    public string GameId { get; set; } = null!;
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = null!;
    public IChannel? TextChannel { get; set; }
    public string AvatarUrl { get; set; } = null!;
    public int Order { get; set; }
    public ImmutableArray<Card> Cards { get; set; }
    public ImmutableArray<Card> CollectedCards { get; set; }
    public int Score { get; set; }
    public int ScoreAdjustment { get; set; }
    public bool SecondMove { get; set; }
    public int Points { get; set; }
}