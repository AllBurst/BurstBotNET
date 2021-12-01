using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Localization.Serializables;

public record RawLocalizations
{
    [JsonPropertyName("jp")] public string Japanese { get; init; } = "";
    [JsonPropertyName("en")] public string English { get; init; } = "";
    [JsonPropertyName("cn")] public string Chinese { get; init; } = "";
};