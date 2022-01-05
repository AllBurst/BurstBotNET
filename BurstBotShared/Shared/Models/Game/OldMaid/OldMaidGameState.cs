using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.OldMaid;

public class OldMaidGameState : 
    IGameState<OldMaidPlayerState, OldMaidGameProgress>, 
    IState<OldMaidGameState, RawOldMaidGameState, OldMaidGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = "";
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, OldMaidPlayerState> Players { get; init; } = new(10, 4);
    public OldMaidGameProgress Progress { get; set; }
    public int CurrentPlayerOrder { get; set; }
    public ulong PreviousPlayerId { get; set; }
    public int TotalBet { get; set; }
    public float BaseBet { get; set; }
    public ImmutableArray<Card> DumpedCards { get; set; } = ImmutableArray<Card>.Empty;
    public Card? PreviouslyDrawnCard { get; set; }
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