using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackInGameResponseEndingData
{
    [JsonPropertyName("progress")] public BlackJackGameProgress Progress { get; init; }
    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";
    [JsonPropertyName("players")] public Dictionary<long, RawBlackJackPlayerState> Players { get; init; } = new();
    [JsonPropertyName("winner")] public RawBlackJackPlayerState? Winner { get; init; }
    [JsonPropertyName("total_rewards")] public int TotalRewards { get; init; }
};