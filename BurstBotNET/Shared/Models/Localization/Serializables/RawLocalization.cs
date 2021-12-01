using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Localization.Serializables;

public record RawLocalization
{
    [JsonPropertyName("bot")] public string Bot { get; init; } = "";
    [JsonPropertyName("blackJack")] public string BlackJack { get; init; } = "";
    [JsonPropertyName("generic")] public string Generic { get; init; } = "";
};