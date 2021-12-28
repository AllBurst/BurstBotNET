#pragma warning disable CA2252
using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineGameState : IState<NinetyNineGameState, RawNinetyNineGameState, NinetyNineGameProgress>
{
    public string GameId { get; set; } = "";
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, NinetyNinePlayerState> Players { get; init; } = new();
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
    public ConcurrentHashSet<DiscordGuild> Guilds { get; } = new();
    
    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => Channel;

    public NinetyNineGameProgress GameProgress
    {
        get => Progress;
        set => Progress = value;
    }
}