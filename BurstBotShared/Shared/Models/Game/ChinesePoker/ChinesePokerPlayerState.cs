using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker;

public class ChinesePokerPlayerState : IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>
{
    public string GameId { get; init; } = "";
    public ulong PlayerId { get; init; }
    public string PlayerName { get; set; } = "";
    public DiscordChannel? TextChannel { get; set; }
    public List<Card> Cards { get; set; } = new();
    public Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> PlayedCards { get; set; } = new();
    public ChinesePokerNatural? Naturals { get; set; } = null;
    public string AvatarUrl { get; set; } = "";

    public Dictionary<ChinesePokerGameProgress, Stream> DeckImages { get; } = new();
    public DiscordMember? Member { get; set; }

    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => null;

    public ChinesePokerGameProgress GameProgress
    {
        get => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
        set => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
    }
}