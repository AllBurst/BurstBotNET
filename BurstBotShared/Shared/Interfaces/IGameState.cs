using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Interfaces;

public interface IGameState<TPlayerState, TProgress>
    where TPlayerState: IPlayerState
    where TProgress: Enum
{
    string GameId { get; set; }
    GameType GameType { get; }
    DateTime LastActiveTime { get; set; }
    ConcurrentDictionary<ulong, TPlayerState> Players { get; init; }
    TProgress Progress { get; set; }
    float BaseBet { get; set; }
    Channel<Tuple<ulong, byte[]>>? RequestChannel { get; set; }
    Channel<byte[]>? ResponseChannel { get; set; }
    SemaphoreSlim Semaphore { get; }
    ConcurrentHashSet<Snowflake> Guilds { get; }
}