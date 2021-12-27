using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerPlayerState : IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>
{
    public string GameId { get; set; } = "";
    public ulong PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public DiscordChannel? TextChannel { get; set; }
    public List<Card> Cards { get; set; } = new();
    public Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> PlayedCards { get; set; } = new();
    public ChinesePokerNatural? Naturals { get; set; } = null;
    public string AvatarUrl { get; set; } = "";

    public Dictionary<ChinesePokerGameProgress, Stream> DeckImages { get; set; } = new();

    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => null;

    public ChinesePokerGameProgress GameProgress
    {
        get => ChinesePokerGameProgress.NotAvailable;
        set {}
    }
}