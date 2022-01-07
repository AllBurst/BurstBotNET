using System.Collections.Concurrent;
using System.Collections.Immutable;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class
    ChinesePokerPlayerState : IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>,
        IPlayerState,
        IDisposable
{
    public string GameId { get; set; } = "";
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = "";
    public IChannel? TextChannel { get; set; }
    public ImmutableArray<Card> Cards { get; set; }
    public Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> PlayedCards { get; set; } = new();
    public ChinesePokerNatural? Naturals { get; set; }
    public string AvatarUrl { get; set; } = "";

    public Dictionary<ChinesePokerGameProgress, Stream> DeckImages { get; } = new();
    public ConcurrentQueue<IMessage?> OutstandingMessages { get; set; } = new();

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
            foreach (var (_, stream) in DeckImages)
                stream.Dispose();

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}