using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game.OldMaid.Serializables;

public record OldMaidInGameRequest : IGenericDealData
{
    [JsonPropertyName("request_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OldMaidInGameRequestType RequestType { get; init; }
    
    [JsonPropertyName("client_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType? ClientType { get; init; }

    [JsonPropertyName("game_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameType GameType { get; init; } = GameType.OldMaid;

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("player_id")] public ulong PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public ulong ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("player_order")] public int? PlayerOrder { get; init; }
    [JsonPropertyName("picked_card_id")] public int? PickedCardId { get; init; }
};