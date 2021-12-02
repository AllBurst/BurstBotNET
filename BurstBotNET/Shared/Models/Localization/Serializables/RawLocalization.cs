using System.Text.Json.Serialization;

namespace BurstBotNET.Shared.Models.Localization.Serializables;

public record RawLocalization
{
    public string Bot { get; init; } = "";
    public string BlackJack { get; init; } = "";
    public string Generic { get; init; } = "";
};