using Newtonsoft.Json;

namespace BurstBotNET.Shared.Models.Data.Serializables;

public record RawTip
{
    [JsonProperty("player_id")] public string PlayerId { get; init; } = "";
    [JsonProperty("amount")] public long Amount { get; init; }
    [JsonProperty("next_daily_reward")] public string? NextDailyReward { get; init; }
    [JsonProperty("next_weekly_reward")] public string? NextWeeklyReward { get; init; }
};