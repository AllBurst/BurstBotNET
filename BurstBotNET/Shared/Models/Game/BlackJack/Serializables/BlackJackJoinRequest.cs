using BurstBotNET.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackJoinRequest
{
    [JsonProperty("client_type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ClientType ClientType { get; init; }
    [JsonProperty("player_ids")] public List<ulong> PlayerIds { get; init; } = new();
};