using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Localization.Serializables;

public record GenericWords
{
    [JsonPropertyName("pronoun")] public string Pronoun { get; init; } = "";

    [JsonPropertyName("participateSecond")]
    public string ParticipateSecond { get; init; } = "";

    [JsonPropertyName("participateThird")] public string ParticipateThird { get; init; } = "";

    [JsonPropertyName("possessiveSecond")] public string PossessiveSecond { get; init; } = "";

    [JsonPropertyName("possessiveThird")] public string PossessiveThird { get; init; } = "";

    [JsonPropertyName("card")] public string Card { get; init; } = "";
};