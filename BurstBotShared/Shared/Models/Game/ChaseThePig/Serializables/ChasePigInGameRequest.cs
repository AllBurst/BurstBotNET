using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;

public record ChasePigInGameRequest : IGenericDealData
{
    [JsonPropertyName("request_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChasePigInGameRequestType RequestType { get; init; }
    
    [JsonPropertyName("client_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType? ClientType { get; init; }
    
    [JsonPropertyName("game_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameType GameType => GameType.ChaseThePig;
    
    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";
    [JsonPropertyName("player_id")] public ulong PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public ulong ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    
    [JsonPropertyName("exposures")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public List<ChasePigExposure>? Exposures { get; init; }
    
    [JsonPropertyName("play_card")] public Card? PlayCard { get; init; }
    [JsonPropertyName("player_order")] public int PlayerOrder { get; init; }
};