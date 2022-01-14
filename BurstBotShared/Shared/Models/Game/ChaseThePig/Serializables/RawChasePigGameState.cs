using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;

public record RawChasePigGameState : IRawState<ChasePigGameState, RawChasePigGameState, ChasePigGameProgress>, IRawGameState<RawChasePigPlayerState, ChasePigGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";

    [JsonPropertyName("last_active_time")]
    [JsonProperty("last_active_time")]
    public string LastActiveTime { get; init; } = "";

    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<ulong, RawChasePigPlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("progress")]
    [JsonProperty("progress")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public ChasePigGameProgress Progress { get; init; }
    
    [JsonPropertyName("base_bet")]
    [JsonProperty("base_bet")]
    public float BaseBet { get; init; }
    
    [JsonPropertyName("previous_player_id")]
    [JsonProperty("previous_player_id")]
    public ulong PreviousPlayerId { get; init; }
    
    [JsonPropertyName("previous_winner")]
    [JsonProperty("previous_winner")]
    public ulong PreviousWinner { get; init; }

    [JsonPropertyName("exposures")]
    [JsonProperty("exposures")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public List<ChasePigExposure> Exposures { get; init; } = new();
    
    [JsonPropertyName("current_player_order")]
    [JsonProperty("current_player_order")]
    public int CurrentPlayerOrder { get; init; }

    public static RawChasePigGameState FromState(IState<ChasePigGameState, RawChasePigGameState, ChasePigGameProgress> state)
    {
        var gameState = (state as ChasePigGameState)!;
        var players = gameState
            .Players
            .ToDictionary(pair => pair.Key, pair => ((IState<ChasePigPlayerState, RawChasePigPlayerState, ChasePigGameProgress>)pair.Value).ToRaw());

        return new RawChasePigGameState
        {
            BaseBet = gameState.BaseBet,
            CurrentPlayerOrder = gameState.CurrentPlayerOrder,
            Exposures = gameState.Exposures.ToList(),
            GameId = gameState.GameId,
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            PreviousPlayerId = gameState.PreviousPlayerId,
            PreviousWinner = gameState.PreviousWinner,
            Progress = gameState.Progress
        };
    }

    public async Task<ChasePigGameState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var playersTask = Players
            .Select(async p => await p.Value.ToState(guildApi, guild));
        var players = (await Task.WhenAll(playersTask))
            .Select(p => KeyValuePair.Create(p.PlayerId, p));

        return new ChasePigGameState
        {
            BaseBet = BaseBet,
            CurrentPlayerOrder = CurrentPlayerOrder,
            Exposures = Exposures.ToImmutableArray(),
            GameId = GameId,
            LastActiveTime = DateTime.Parse(LastActiveTime),
            Players = new ConcurrentDictionary<ulong, ChasePigPlayerState>(players),
            PreviousPlayerId = PreviousPlayerId,
            PreviousWinner = PreviousWinner,
            Progress = Progress
        };
    }
}