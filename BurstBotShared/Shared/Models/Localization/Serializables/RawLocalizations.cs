namespace BurstBotShared.Shared.Models.Localization.Serializables;

public record RawLocalizations
{
    public string Japanese { get; init; } = "";
    public string English { get; init; } = "";
    public string Chinese { get; init; } = "";
};