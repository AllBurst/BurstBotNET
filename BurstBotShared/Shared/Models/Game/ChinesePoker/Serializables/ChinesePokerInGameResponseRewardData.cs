using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record ChinesePokerInGameResponseRewardData
{
    [JsonPropertyName("units")]
    public int Units { get; init; }

    [JsonPropertyName("rewards")]
    public List<ChinesePokerInGameResponseRewardWrapper> Rewards { get; init; } = new();
};