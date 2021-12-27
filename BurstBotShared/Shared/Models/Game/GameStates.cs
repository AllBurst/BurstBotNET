using System.Collections.Concurrent;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChinesePoker;

namespace BurstBotShared.Shared.Models.Game;

public class GameStates
{
    public Tuple<ConcurrentDictionary<string, BlackJackGameState>, HashSet<ulong>> BlackJackGameStates { get; set; } =
        new(
            new ConcurrentDictionary<string, BlackJackGameState>(10, 100), new HashSet<ulong>());

    public Tuple<ConcurrentDictionary<string, ChinesePokerGameState>, HashSet<ulong>> ChinesePokerGameStates
    {
        get;
        set;
    } =
        new(
            new ConcurrentDictionary<string, ChinesePokerGameState>(10, 100), new HashSet<ulong>());
}