using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;

public record RawRedDotsGameState : IRawState<RedDotsGameState, RawRedDotsGameState, RedDotsGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";

    [JsonPropertyName("last_active_time")]
    [JsonProperty("last_active_time")]
    public string LastActiveTime { get; init; } = null!;
    
    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<ulong, RawRedDotsPlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("progress")]
    [JsonProperty("progress")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public RedDotsGameProgress Progress { get; init; }
    
    [JsonPropertyName("base_bet")]
    [JsonProperty("base_bet")]
    public float BaseBet { get; init; }
    
    [JsonPropertyName("current_player_order")]
    [JsonProperty("current_player_order")]
    public int CurrentPlayerOrder { get; init; }
    
    [JsonPropertyName("previous_player_id")]
    [JsonProperty("previous_player_id")]
    public ulong PreviousPlayerId { get; init; }
    
    [JsonPropertyName("cards_on_table")]
    [JsonProperty("cards_on_table")]
    public List<Card> CardsOnTable { get; init; } = new();

    public static RawRedDotsGameState FromState(IState<RedDotsGameState, RawRedDotsGameState, RedDotsGameProgress> state)
    {
        var gameState = (state as RedDotsGameState)!;
        var players = gameState.Players
            .ToDictionary(pair => pair.Key,
                pair => ((IState<RedDotsPlayerState, RawRedDotsPlayerState, RedDotsGameProgress>)pair.Value).ToRaw());
        return new RawRedDotsGameState
        {
            BaseBet = gameState.BaseBet,
            CardsOnTable = gameState.CardsOnTable.ToList(),
            CurrentPlayerOrder = gameState.CurrentPlayerOrder,
            GameId = gameState.GameId,
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            PreviousPlayerId = gameState.PreviousPlayerId,
            Progress = gameState.Progress
        };
    }

    public async Task<RedDotsGameState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var playersTask = Players
            .Values
            .Select(async p =>
            {
                var playerState = await p.ToState(guildApi, guild);
                return KeyValuePair.Create(p.PlayerId, playerState);
            });
        var players = await Task.WhenAll(playersTask);

        return new RedDotsGameState
        {
            BaseBet = BaseBet,
            CardsOnTable = CardsOnTable.ToImmutableArray(),
            CurrentPlayerOrder = CurrentPlayerOrder,
            GameId = GameId,
            LastActiveTime = DateTime.Parse(LastActiveTime),
            Players = new ConcurrentDictionary<ulong, RedDotsPlayerState>(players),
            PreviousPlayerId = PreviousPlayerId,
            Progress = Progress
        };
    }
};