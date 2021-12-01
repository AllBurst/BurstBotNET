using System.Text.Json.Serialization;
using BurstBotNET.Shared.Models.Game.Serializables;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackJoinRequest
{
    [JsonPropertyName("client_type")] public ClientType ClientType { get; init; }
    [JsonPropertyName("player_ids")] public List<long> PlayerIds { get; init; } = new();
};