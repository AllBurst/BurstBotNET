using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.Serializables;

public record GenericJoinRequest
{
    [JsonProperty("client_type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ClientType ClientType { get; init; }
    
    [JsonProperty("game_type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public GameType GameType { get; init; }
    
    [JsonProperty("player_ids")] public List<ulong> PlayerIds { get; init; } = new();
};