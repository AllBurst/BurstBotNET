using System.Collections.Concurrent;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.RedDotsPicking;
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
    
    public Tuple<ConcurrentDictionary<string, NinetyNineGameState>, ConcurrentHashSet<Snowflake>> NinetyNineGameStates
    {
        get;
        set;
    } = new(new ConcurrentDictionary<string, NinetyNineGameState>(10, 100), new ConcurrentHashSet<Snowflake>());

    public Tuple<ConcurrentDictionary<string, OldMaidGameState>, ConcurrentHashSet<Snowflake>> OldMaidGameStates
    {
        get;
        set;
    } = new(new ConcurrentDictionary<string, OldMaidGameState>(10, 100), new ConcurrentHashSet<Snowflake>());
    
    public Tuple<ConcurrentDictionary<string, RedDotsGameState>, ConcurrentHashSet<Snowflake>> RedDotsGameStates
    {
        get;
        set;
    } = new(new ConcurrentDictionary<string, RedDotsGameState>(10, 100), new ConcurrentHashSet<Snowflake>());
}