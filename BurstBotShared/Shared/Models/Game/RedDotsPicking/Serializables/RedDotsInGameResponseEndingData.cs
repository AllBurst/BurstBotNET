using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;

public record RedDotsInGameResponseEndingData
{
    [JsonPropertyName("progress")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RedDotsGameProgress Progress { get; init; }

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("players")] public Dictionary<ulong, RawRedDotsPlayerState> Players { get; init; } = new();
    [JsonPropertyName("rewards")] public Dictionary<ulong, int> Rewards { get; init; } = new();
};