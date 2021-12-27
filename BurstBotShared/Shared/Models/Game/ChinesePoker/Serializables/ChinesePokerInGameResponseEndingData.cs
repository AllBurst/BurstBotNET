using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record ChinesePokerInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChinesePokerGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")]
    public string GameId { get; init; } = null!;

    [JsonPropertyName("players")]
    public Dictionary<ulong, RawChinesePokerPlayerState> Players { get; init; } = new();

    [JsonPropertyName("total_rewards")]
    public Dictionary<ulong, ChinesePokerInGameResponseRewardData> TotalRewards { get; init; } = new();
    
    [JsonPropertyName("declared_natural")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChinesePokerNatural? DeclaredNatural { get; init; }
};