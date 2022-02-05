using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerGameState : 
    IState<ChinesePokerGameState, RawChinesePokerGameState, ChinesePokerGameProgress>,
    IGameState<ChinesePokerPlayerState, ChinesePokerGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = "";
    public GameType GameType => GameType.ChinesePoker;
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, ChinesePokerPlayerState> Players { get; init; } = new(10, 4);
    public ChinesePokerGameProgress Progress { get; set; }
    public float BaseBet { get; set; }
    public Dictionary<ulong, Dictionary<ulong, int>> Units { get; set; } = new();
    public ulong PreviousPlayerId { get; init; }
    public bool DebugNatural { get; set; }
    public Channel<Tuple<ulong, byte[]>>? RequestChannel { get; set; }
    public Channel<byte[]>? ResponseChannel { get; set; }
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public ConcurrentHashSet<Snowflake> Guilds { get; } = new();

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            foreach (var (_, player) in Players)
                player.Dispose();
            
            Semaphore.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}