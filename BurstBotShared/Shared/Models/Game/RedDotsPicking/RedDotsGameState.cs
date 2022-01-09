using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking;

public class RedDotsGameState : 
    IState<RedDotsGameState, RawRedDotsGameState, RedDotsGameProgress>,
    IGameState<RedDotsPlayerState, RedDotsGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = null!;
    public DateTime LastActiveTime { get; set; }
    public ConcurrentDictionary<ulong, RedDotsPlayerState> Players { get; init; } = new(10, 4);
    public RedDotsGameProgress Progress { get; set; }
    public float BaseBet { get; set; }
    public int CurrentPlayerOrder { get; set; }
    public int PreviousPlayerId { get; set; }
    public ImmutableArray<Card> CardsOnTable { get; set; }
    public Channel<Tuple<ulong, byte[]>>? Channel { get; set; }
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public ConcurrentHashSet<Snowflake> Guilds { get; } = new();

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
            Semaphore.Dispose();

        _disposed = true;
    }
}