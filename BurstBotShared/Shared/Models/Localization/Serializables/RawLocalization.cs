namespace BurstBotShared.Shared.Models.Localization.Serializables;

public record RawLocalization
{
    public string Bot { get; init; } = "";
    public string BlackJack { get; init; } = "";
    public string ChinesePoker { get; init; } = "";
    public string NinetyNine { get; init; } = "";
    public string OldMaid { get; init; } = "";
    public string RedDotsPicking { get; init; } = "";
    public string ChaseThePig { get; init; } = "";
    public string Generic { get; init; } = "";
}