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

namespace BurstBotShared.Shared.Models.Game.OldMaid.Serializables;

public record RawOldMaidGameState : IRawState<OldMaidGameState, RawOldMaidGameState, OldMaidGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";

    [JsonPropertyName("last_active_time")]
    [JsonProperty("last_active_time")]
    public string LastActiveTime { get; init; } = "";

    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<ulong, RawOldMaidPlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("progress")]
    [JsonProperty("progress")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public OldMaidGameProgress Progress { get; init; }
    
    [JsonPropertyName("current_player_order")]
    [JsonProperty("current_player_order")]
    public int CurrentPlayerOrder { get; init; }
    
    [JsonPropertyName("previous_player_id")]
    [JsonProperty("previous_player_id")]
    public ulong PreviousPlayerId { get; init; }
    
    [JsonPropertyName("total_bet")]
    [JsonProperty("total_bet")]
    public int TotalBet { get; init; }
    
    [JsonPropertyName("base_bet")]
    [JsonProperty("base_bet")]
    public float BaseBet { get; init; }

    [JsonPropertyName("dumped_cards")]
    [JsonProperty("dumped_cards")]
    public List<Card> DumpedCards { get; init; } = new();
    
    [JsonPropertyName("previously_drawn_card")]
    [JsonProperty("previously_drawn_card")]
    public Card? PreviouslyDrawnCard { get; init; }

    public static RawOldMaidGameState FromState(IState<OldMaidGameState, RawOldMaidGameState, OldMaidGameProgress> state)
    {
        var gameState = (state as OldMaidGameState)!;
        var players = gameState.Players
            .ToDictionary(pair => pair.Key,
                pair => ((IState<OldMaidPlayerState, RawOldMaidPlayerState, OldMaidGameProgress>)pair.Value).ToRaw());
        return new RawOldMaidGameState
        {
            GameId = gameState.GameId,
            BaseBet = gameState.BaseBet,
            CurrentPlayerOrder = gameState.CurrentPlayerOrder,
            DumpedCards = gameState.DumpedCards.ToList(),
            LastActiveTime = gameState.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = players,
            PreviousPlayerId = gameState.PreviousPlayerId,
            Progress = gameState.Progress,
            TotalBet = gameState.TotalBet,
            PreviouslyDrawnCard = gameState.PreviouslyDrawnCard
        };
    }

    public async Task<OldMaidGameState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var playersTask = Players
            .Values
            .Select(async p =>
            {
                var playerState = await p.ToState(guildApi, guild);
                return KeyValuePair.Create(p.PlayerId, playerState);
            });
        var players = await Task.WhenAll(playersTask);
        
        return new OldMaidGameState
        {
            GameId = GameId,
            BaseBet = BaseBet,
            CurrentPlayerOrder = CurrentPlayerOrder,
            DumpedCards = DumpedCards.ToImmutableArray(),
            LastActiveTime = DateTime.Parse(LastActiveTime),
            Players = new ConcurrentDictionary<ulong, OldMaidPlayerState>(players),
            PreviousPlayerId = PreviousPlayerId,
            Progress = Progress,
            TotalBet = TotalBet,
            PreviouslyDrawnCard = PreviouslyDrawnCard
        };
    }
};