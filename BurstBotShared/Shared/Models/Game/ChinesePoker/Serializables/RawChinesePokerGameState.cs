using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record RawChinesePokerGameState : IRawState<ChinesePokerGameState, RawChinesePokerGameState, ChinesePokerGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";
    
    [JsonPropertyName("last_active_time")]
    [JsonProperty("last_active_time")]
    public string LastActiveTime { get; init; } = "";

    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<ulong, RawChinesePokerPlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("progress")]
    [JsonProperty("progress")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public ChinesePokerGameProgress Progress { get; init; }
    
    [JsonPropertyName("base_bet")]
    [JsonProperty("base_bet")]
    public float BaseBet { get; init; }

    [JsonPropertyName("units")]
    [JsonProperty("units")]
    public Dictionary<ulong, Dictionary<ulong, int>> Units { get; init; } = new();
    
    [JsonPropertyName("previous_player_id")]
    [JsonProperty("previous_player_id")] 
    public ulong PreviousPlayerId { get; init; }

    public static RawChinesePokerGameState FromState(IState<ChinesePokerGameState, RawChinesePokerGameState, ChinesePokerGameProgress> state)
    {
        var gameState = state as ChinesePokerGameState;
        var players = gameState!.Players
            .ToDictionary(pair => pair.Key, pair =>
                ((IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>)pair.Value).ToRaw());
        return new RawChinesePokerGameState
        {
            GameId = gameState!.GameId,
            BaseBet = gameState.BaseBet,
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            Progress = gameState.Progress,
            Units = gameState.Units,
            PreviousPlayerId = gameState.PreviousPlayerId
        };
    }

    public async Task<ChinesePokerGameState> ToState(DiscordGuild guild)
    {
        var players = new List<KeyValuePair<ulong, ChinesePokerPlayerState>>(Players.Count);
        foreach (var (k, v) in Players)
        {
            var playerState = await v.ToState(guild);
            players.Add(new KeyValuePair<ulong, ChinesePokerPlayerState>(k, playerState));
        }

        return new ChinesePokerGameState
        {
            BaseBet = BaseBet,
            GameId = GameId,
            LastActiveTime = DateTime.Parse(LastActiveTime, CultureInfo.InvariantCulture),
            Players = new ConcurrentDictionary<ulong, ChinesePokerPlayerState>(players),
            Progress = Progress,
            Units = Units,
            PreviousPlayerId = PreviousPlayerId
        };
    }
};