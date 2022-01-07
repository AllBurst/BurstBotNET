using Newtonsoft.Json;

namespace BurstBotShared.Shared.Models.Data.Serializables;

public record NewPlayer
{
    [JsonProperty("player_id")] public string PlayerId { get; init; } = "";
    [JsonProperty("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonProperty("name")] public string Name { get; init; } = "";
};