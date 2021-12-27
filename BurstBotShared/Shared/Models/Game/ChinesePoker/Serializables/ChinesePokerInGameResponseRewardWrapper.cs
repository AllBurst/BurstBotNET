using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record ChinesePokerInGameResponseRewardWrapper
{
    [JsonPropertyName("reward_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChinesePokerInGameResponseRewardType RewardType { get; init; }
    
    [JsonPropertyName("natural")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChinesePokerNatural? Natural { get; init; }
};