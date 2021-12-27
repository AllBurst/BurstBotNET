using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record ChinesePokerInGameRequest
{
    [JsonPropertyName("request_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChinesePokerInGameRequestType RequestType { get; init; }
    
    [JsonPropertyName("client_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType? ClientType { get; init; }
    
    [JsonPropertyName("game_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameType GameType { get; init; } = GameType.ChinesePoker;

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("player_id")] public ulong PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public ulong ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("base_bet")] public float BaseBet { get; init; }
    [JsonPropertyName("play_card")] public ImmutableArray<Card>? PlayCard { get; init; }
    [JsonPropertyName("declared_natural")] public ChinesePokerNatural? DeclaredNatural { get; init; }
};