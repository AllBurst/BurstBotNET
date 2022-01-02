using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackGameState :
    IState<BlackJackGameState, RawBlackJackGameState, BlackJackGameProgress>,
    IGameState<BlackJackPlayerState, BlackJackGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = "";
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, BlackJackPlayerState> Players { get; init; } = new(10, 6);
    public BlackJackGameProgress Progress { get; set; } = BlackJackGameProgress.NotAvailable;
    public int CurrentPlayerOrder { get; set; }
    public ulong PreviousPlayerId { get; set; }
    public string PreviousRequestType { get; set; } = "";
    public int HighestBet { get; set; }
    public int CurrentTurn { get; set; }
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