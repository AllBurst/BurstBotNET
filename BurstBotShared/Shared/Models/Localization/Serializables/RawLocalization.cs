namespace BurstBotShared.Shared.Models.Localization.Serializables;

public record RawLocalization
{
    public string Bot { get; init; } = "";
    public string BlackJack { get; init; } = "";
    public string ChinesePoker { get; init; } = "";
    public string Generic { get; init; } = "";
}