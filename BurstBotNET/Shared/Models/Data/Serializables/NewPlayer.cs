using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Data.Serializables;

public record NewPlayer
{
    [JsonPropertyName("player_id")] public string PlayerId { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
};