using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackInGameRequest
{
    [JsonPropertyName("request_type")] 
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BlackJackInGameRequestType RequestType { get; init; }
    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";

    [JsonPropertyName("game_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameType GameType { get; init; } = GameType.BlackJack;
    
    [JsonPropertyName("player_id")] public ulong PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public ulong ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("own_tips")] public long? OwnTips { get; init; }
    
    [JsonPropertyName("client_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType? ClientType { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("cards")] public ImmutableList<Card>? Cards { get; init; }
    [JsonPropertyName("bets")] public int? Bets { get; init; }
};