#pragma warning disable CA2252
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;

public record RawNinetyNineGameState : IRawState<NinetyNineGameState, RawNinetyNineGameState, NinetyNineGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";

    [JsonPropertyName("last_active_time")]
    [JsonProperty("last_active_time")]
    public string LastActiveTime { get; init; } = "";

    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<ulong, RawNinetyNinePlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("progress")]
    [JsonProperty("progress")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public NinetyNineGameProgress Progress { get; init; }
    
    [JsonPropertyName("base_bet")]
    [JsonProperty("base_bet")]
    public float BaseBet { get; init; }
    
    [JsonPropertyName("current_player_order")]
    [JsonProperty("current_player_order")]
    public int CurrentPlayerOrder { get; init; }
    
    [JsonPropertyName("previous_player_id")]
    [JsonProperty("previous_player_id")]
    public ulong PreviousPlayerId { get; init; }
    
    [JsonPropertyName("current_total")]
    [JsonProperty("current_total")]
    public ushort CurrentTotal { get; init; }
    
    [JsonPropertyName("previous_cards")]
    [JsonProperty("previous_cards")]
    public List<Card>? PreviousCards { get; init; }
    
    [JsonPropertyName("variation")]
    [JsonProperty("variation")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public NinetyNineVariation Variation { get; init; }
    
    [JsonPropertyName("difficulty")]
    [JsonProperty("difficulty")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public NinetyNineDifficulty Difficulty { get; init; }
    
    [JsonPropertyName("total_bet")]
    [JsonProperty("total_bet")]
    public int TotalBet { get; init; }

    [JsonPropertyName("burst_players")]
    [JsonProperty("burst_players")]
    public List<ulong> BurstPlayers { get; init; } = new();

    [JsonPropertyName("consecutive_queens")]
    [JsonProperty("consecutive_queens")]
    public List<Card> ConsecutiveQueens { get; init; } = new();

    [Pure]
    public static RawNinetyNineGameState FromState(IState<NinetyNineGameState, RawNinetyNineGameState, NinetyNineGameProgress> state)
    {
        var gameState = state as NinetyNineGameState;
        var players = gameState!
            .Players
            .ToDictionary(pair => pair.Key,
                pair => ((IState<NinetyNinePlayerState, RawNinetyNinePlayerState, NinetyNineGameProgress>)pair.Value)
                    .ToRaw());
        return new RawNinetyNineGameState
        {
            CurrentPlayerOrder = gameState.CurrentPlayerOrder,
            CurrentTotal = gameState.CurrentTotal,
            Difficulty = gameState.Difficulty,
            GameId = gameState.GameId,
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            PreviousCards = gameState.PreviousCards.ToList(),
            PreviousPlayerId = gameState.PreviousPlayerId,
            Progress = gameState.Progress,
            TotalBet = gameState.TotalBet,
            Variation = gameState.Variation,
            BaseBet = gameState.BaseBet,
            BurstPlayers = gameState.BurstPlayers.ToList(),
            ConsecutiveQueens = gameState.ConsecutiveQueens.ToList()
        };
    }

    public async Task<NinetyNineGameState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var playersTask = Players
            .Values
            .Select(async p =>
            {
                var playerState = await p.ToState(guildApi, guild);
                return KeyValuePair.Create(playerState.PlayerId, playerState);
            });
        var players = await Task.WhenAll(playersTask);
        
        return new NinetyNineGameState
        {
            CurrentPlayerOrder = CurrentPlayerOrder,
            CurrentTotal = CurrentTotal,
            Difficulty = Difficulty,
            GameId = GameId,
            Progress = Progress,
            LastActiveTime = DateTime.Parse(LastActiveTime),
            Players = new ConcurrentDictionary<ulong, NinetyNinePlayerState>(players),
            PreviousCards = PreviousCards?.ToImmutableArray() ?? ImmutableArray<Card>.Empty,
            Variation = Variation,
            TotalBet = TotalBet,
            PreviousPlayerId = PreviousPlayerId,
            BaseBet = BaseBet,
            BurstPlayers = BurstPlayers.ToImmutableArray(),
            ConsecutiveQueens = ConsecutiveQueens.ToImmutableArray()
        };
    }
};