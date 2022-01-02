#pragma warning disable CA2252
using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineGameState :
    IState<NinetyNineGameState, RawNinetyNineGameState, NinetyNineGameProgress>,
    IGameState<NinetyNinePlayerState, NinetyNineGameProgress>,
    IDisposable
{
    public string GameId { get; set; } = "";
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, NinetyNinePlayerState> Players { get; init; } = new(10, 8);
    public NinetyNineGameProgress Progress { get; set; }
    public int CurrentPlayerOrder { get; set; }
    public ulong PreviousPlayerId { get; init; }
    public ushort CurrentTotal { get; set; }
    public Card? PreviousCard { get; set; }
    public NinetyNineVariation Variation { get; set; }
    public NinetyNineDifficulty Difficulty { get; set; }
    public int TotalBet { get; set; }
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