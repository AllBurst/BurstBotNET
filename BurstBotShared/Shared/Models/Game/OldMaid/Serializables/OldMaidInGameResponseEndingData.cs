using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.OldMaid.Serializables;

public record OldMaidInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OldMaidGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("players")] public Dictionary<ulong, OldMaidPlayerState> Players { get; init; } = new();
    [JsonPropertyName("loser")] public OldMaidPlayerState? Loser { get; init; }
    [JsonPropertyName("total_rewards")] public int TotalRewards { get; init; }
};