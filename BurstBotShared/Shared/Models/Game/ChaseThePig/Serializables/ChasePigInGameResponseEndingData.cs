using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;

public record ChasePigInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChasePigGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";
    [JsonPropertyName("players")] public Dictionary<ulong, RawChasePigPlayerState> Players { get; init; } = new();
    [JsonPropertyName("rewards")] public Dictionary<ulong, int> Rewards { get; init; } = new();
};