using System.Collections.Concurrent;
using System.Threading.Channels;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Interfaces;

public interface IGameState<TPlayerState, TProgress>
    where TPlayerState: IPlayerState
    where TProgress: Enum
{
    ConcurrentDictionary<ulong, TPlayerState> Players { get; init; }
    TProgress Progress { get; set; }
    Channel<Tuple<ulong, byte[]>>? Channel { get; set; }
    SemaphoreSlim Semaphore { get; }
    ConcurrentHashSet<Snowflake> Guilds { get; }
}