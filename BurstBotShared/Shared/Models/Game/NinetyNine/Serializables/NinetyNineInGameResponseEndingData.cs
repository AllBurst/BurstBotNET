using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;

public record NinetyNineInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NinetyNineGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("players")] public Dictionary<ulong, NinetyNinePlayerState> Players { get; init; } = new();
    [JsonPropertyName("rewards")] public Dictionary<ulong, int> Rewards { get; init; } = new();
}
