using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;

public record NinetyNineInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NinetyNineGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("players")] public Dictionary<ulong, RawNinetyNinePlayerState> Players { get; init; } = new();
    [JsonPropertyName("winner")] public RawNinetyNinePlayerState? Winner { get; init; }
    [JsonPropertyName("total_rewards")] public uint TotalRewards { get; init; } = new();
}
