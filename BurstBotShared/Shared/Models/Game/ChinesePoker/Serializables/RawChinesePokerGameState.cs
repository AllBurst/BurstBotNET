using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record
    RawChinesePokerGameState : IRawState<ChinesePokerGameState, RawChinesePokerGameState, ChinesePokerGameProgress>
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

    [JsonPropertyName("debug_natural")]
    [JsonProperty("debug_natural")]
    public bool DebugNatural { get; init; }

    [Pure]
    public static RawChinesePokerGameState FromState(
        IState<ChinesePokerGameState, RawChinesePokerGameState, ChinesePokerGameProgress> state)
    {
        var gameState = state as ChinesePokerGameState;
        var players = gameState!.Players
            .ToDictionary(pair => pair.Key, pair =>
                ((IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress>)pair.Value)
                .ToRaw());
        return new RawChinesePokerGameState
        {
            GameId = gameState.GameId,
            BaseBet = gameState.BaseBet,
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            Progress = gameState.Progress,
            Units = gameState.Units,
            PreviousPlayerId = gameState.PreviousPlayerId,
            DebugNatural = gameState.DebugNatural
        };
    }

    public async Task<ChinesePokerGameState> ToState(DiscordGuild guild)
    {
        var playersTask = Players.Values
            .Select(async p =>
            {
                var playerState = await p.ToState(guild);
                return KeyValuePair.Create(playerState.PlayerId, playerState);
            });
        var players = await Task.WhenAll(playersTask);

        return new ChinesePokerGameState
        {
            BaseBet = BaseBet,
            GameId = GameId,
            LastActiveTime = DateTime.Parse(LastActiveTime, CultureInfo.InvariantCulture),
            Players = new ConcurrentDictionary<ulong, ChinesePokerPlayerState>(players),
            Progress = Progress,
            Units = Units,
            PreviousPlayerId = PreviousPlayerId,
            DebugNatural = DebugNatural
        };
    }
}