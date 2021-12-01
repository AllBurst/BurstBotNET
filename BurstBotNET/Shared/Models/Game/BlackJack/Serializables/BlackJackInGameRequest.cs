using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotNET.Shared.Models.Game.Serializables;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackInGameRequest
{
    [JsonPropertyName("request_type")] public BlackJackInGameRequestType RequestType { get; init; }
    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";
    [JsonPropertyName("player_id")] public long PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public long? ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("own_tips")] public long? OwnTips { get; init; }
    [JsonPropertyName("client_type")] public ClientType? ClientType { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("cards")] public ImmutableList<Card>? Cards { get; init; }
    [JsonPropertyName("bets")] public int? Bets { get; init; }
};