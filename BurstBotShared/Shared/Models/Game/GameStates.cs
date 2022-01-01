using System.Collections.Concurrent;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using ConcurrentCollections;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game;

public class GameStates
{
    public Tuple<ConcurrentDictionary<string, BlackJackGameState>, ConcurrentHashSet<Snowflake>> BlackJackGameStates { get; set; } =
        new(
            new ConcurrentDictionary<string, BlackJackGameState>(10, 100), new ConcurrentHashSet<Snowflake>());

    public Tuple<ConcurrentDictionary<string, ChinesePokerGameState>, ConcurrentHashSet<Snowflake>> ChinesePokerGameStates
    {
        get;
        set;
    } =
        new(
            new ConcurrentDictionary<string, ChinesePokerGameState>(10, 100), new ConcurrentHashSet<Snowflake>());
}