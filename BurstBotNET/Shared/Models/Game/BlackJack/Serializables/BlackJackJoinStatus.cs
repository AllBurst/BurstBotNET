using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackJoinStatus
{
    [JsonPropertyName("type")] public BlackJackJoinStatusType StatusType { get; init; }
    [JsonPropertyName("socket_identifier")] public string? SocketIdentifier { get; init; }
    [JsonPropertyName("game_id")] public string? GameId { get; init; }
    [JsonPropertyName("player_ids")] public List<long> PlayerIds { get; init; } = new();
};