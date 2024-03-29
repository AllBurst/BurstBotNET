using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace BurstBotShared.Shared.Models.Data.Serializables;

public record RewardResponse
{
    [JsonProperty("type")]
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";
    
    [JsonProperty("player_id")]
    [JsonPropertyName("player_id")]
    public string PlayerId { get; init; } = "";
    
    [JsonProperty("avatar_url")]
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }
    
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    
    [JsonProperty("amount")]
    [JsonPropertyName("amount")]
    public long Amount { get; init; }
    
    [JsonProperty("next_daily_reward")]
    [JsonPropertyName("next_daily_reward")]
    public string? NextDailyReward { get; init; }
    
    [JsonProperty("next_weekly_reward")]
    [JsonPropertyName("next_weekly_reward")]
    public string? NextWeeklyReward { get; init; }
};