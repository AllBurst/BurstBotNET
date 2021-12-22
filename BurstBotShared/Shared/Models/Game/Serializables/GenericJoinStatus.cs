using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.Serializables;

public record GenericJoinStatus
{
    [JsonPropertyName("type")]
    [JsonProperty("type")]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public GenericJoinStatusType StatusType { get; init; }
    
    [JsonPropertyName("socket_identifier")]
    [JsonProperty("socket_identifier")]
    public string? SocketIdentifier { get; init; }
    
    [JsonPropertyName("game_id")] 
    [JsonProperty("game_id")] 
    public string? GameId { get; init; }
    
    [JsonPropertyName("game_type")]
    [JsonProperty("game_type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public GameType GameType { get; init; }
    
    [JsonPropertyName("player_ids")] 
    [JsonProperty("player_ids")] 
    public List<ulong> PlayerIds { get; init; } = new();
};