using System.Collections.Concurrent;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using ConcurrentCollections;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackGameState : IState<BlackJackGameState, RawBlackJackGameState, BlackJackGameProgress>
{
    public string GameId { get; set; } = "";
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public ConcurrentDictionary<ulong, BlackJackPlayerState> Players { get; set; } = new(10, 6);
    public BlackJackGameProgress Progress { get; set; } = BlackJackGameProgress.NotAvailable;
    public int CurrentPlayerOrder { get; set; }
    public ulong PreviousPlayerId { get; set; }
    public string PreviousRequestType { get; set; } = "";
    public int HighestBet { get; set; }
    public int CurrentTurn { get; set; }
    public Channel<Tuple<ulong, byte[]>>? Channel { get; set; }
    public readonly SemaphoreSlim Semaphore = new(1, 1);
    public readonly ConcurrentHashSet<DiscordGuild> Guilds = new();
    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => Channel;

    public BlackJackGameProgress GameProgress
    {
        get => Progress;
        set => Progress = value;
    }
}