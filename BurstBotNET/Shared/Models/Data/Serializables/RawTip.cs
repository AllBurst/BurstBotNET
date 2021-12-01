using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Data.Serializables;

public record RawTip
{
    [JsonPropertyName("player_id")] public string PlayerId { get; init; } = "";
    [JsonPropertyName("amount")] public long Amount { get; init; }
    [JsonPropertyName("next_daily_reward")] public string? NextDailyReward { get; init; }
    [JsonPropertyName("next_weekly_reward")] public string? NextWeeklyReward { get; init; }
};