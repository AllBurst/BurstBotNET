using System.Collections.Concurrent;
using BurstBotNET.Shared.Models.Game.BlackJack;

namespace BurstBotNET.Shared.Models.Game;

public class GameStates
{
    public Tuple<ConcurrentDictionary<string, BlackJackGameState>, HashSet<ulong>> BlackJackGameStates { get; set; } =
        new(
            new ConcurrentDictionary<string, BlackJackGameState>(10, 100), new HashSet<ulong>());
}