using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig;

public class ChasePigGameState :
    IState<ChasePigGameState, RawChasePigGameState, ChasePigGameProgress>,
    IGameState<ChasePigPlayerState, ChasePigGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = null!;
    public GameType GameType => GameType.ChaseThePig;
    public DateTime LastActiveTime { get; set; }
    public ConcurrentDictionary<ulong, ChasePigPlayerState> Players { get; init; } = new(10, 4);
    public ChasePigGameProgress Progress { get; set; }
    public float BaseBet { get; set; }
    public ulong PreviousPlayerId { get; set; }
    public ulong PreviousWinner { get; set; }
    public ImmutableArray<ChasePigExposure> Exposures { get; set; }
    public int CurrentPlayerOrder { get; set; }

    public List<(ulong, Card)> CardsOnTable { get; } = new();
    
    public Channel<Tuple<ulong, byte[]>>? RequestChannel { get; set; }
    public Channel<byte[]>? ResponseChannel { get; set; }
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