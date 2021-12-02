using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackInGameResponseEndingData
{
    [JsonPropertyName("progress")] 
    [JsonProperty("progress")] 
    public BlackJackGameProgress Progress { get; init; }
    
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";
    
    [JsonPropertyName("players")]
    [JsonProperty("players")]
    public Dictionary<long, RawBlackJackPlayerState> Players { get; init; } = new();
    
    [JsonPropertyName("winner")]
    [JsonProperty("winner")]
    public RawBlackJackPlayerState? Winner { get; init; }
    
    [JsonPropertyName("total_rewards")]
    [JsonProperty("total_rewards")]
    public int TotalRewards { get; init; }
};