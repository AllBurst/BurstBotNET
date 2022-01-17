using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Localization.Serializables;

public record GenericWords
{
    [JsonPropertyName("pronoun")] public string Pronoun { get; init; } = "";

    [JsonPropertyName("participateSecond")]
    public string ParticipateSecond { get; init; } = "";

    [JsonPropertyName("participateThird")] public string ParticipateThird { get; init; } = "";

    [JsonPropertyName("possessiveSecond")] public string PossessiveSecond { get; init; } = "";

    [JsonPropertyName("possessiveThird")] public string PossessiveThird { get; init; } = "";

    [JsonPropertyName("card")] public string Card { get; init; } = "";
    [JsonPropertyName("from")] public string From { get; init; } = "";
    [JsonPropertyName("confirm")] public string Confirm { get; init; } = "";
};