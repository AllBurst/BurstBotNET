using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class
    ChinesePokerPlayerState : IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>
{
    public string GameId { get; init; } = "";
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = "";
    public IChannel? TextChannel { get; set; }
    public ImmutableArray<Card> Cards { get; set; }
    public Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> PlayedCards { get; set; } = new();
    public ChinesePokerNatural? Naturals { get; set; }
    public string AvatarUrl { get; set; } = "";

    public Dictionary<ChinesePokerGameProgress, Stream> DeckImages { get; } = new();
    public IGuildMember? Member { get; set; }
    public ConcurrentQueue<(IMessage?, IMessage?)> OutstandingMessages { get; set; } = new();

    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => null;

    public ChinesePokerGameProgress GameProgress
    {
        get => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
        set => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
    }
}