using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;

namespace BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;

public record ChinesePokerLocalization : ILocalization<ChinesePokerLocalization>
{
    [JsonPropertyName("commandList")] public Dictionary<string, string> CommandList { get; init; } = new();

    [JsonPropertyName("initialMessagePrefix")]
    public string InitialMessagePrefix { get; init; } = "";
    
    [JsonPropertyName("initialMessageDescription")]
    public string InitialMessageDescription { get; init; } = "";

    [JsonPropertyName("initialMessageTitle")]
    public string InitialMessageTitle { get; init; } = "";

    [JsonPropertyName("initialMessageFooter")]
    public string InitialMessageFooter { get; init; } = "";
    
    [JsonPropertyName("winTitle")] public string WinTitle { get; init; } = "";
    [JsonPropertyName("winDescription")] public string WinDescription { get; init; } = "";
    [JsonPropertyName("won")] public string Won { get; init; } = "";
    [JsonPropertyName("lost")] public string Lost { get; init; } = "";
    [JsonPropertyName("allUnits")] public string AllUnits { get; init; } = "";

    [JsonPropertyName("frontHand")] public string FrontHand { get; init; } = "";
    [JsonPropertyName("middleHand")] public string MiddleHand { get; init; } = "";
    [JsonPropertyName("backHand")] public string BackHand { get; init; } = "";
    [JsonPropertyName("cards")] public string Cards { get; init; } = "";
    [JsonPropertyName("invalidCard")] public string InvalidCard { get; init; } = "";
    [JsonPropertyName("confirmCards")] public string ConfirmCards { get; init; } = "";

    [JsonPropertyName("confirmCardsFooter")]
    public string ConfirmCardsFooter { get; init; } = "";

    [JsonPropertyName("confirmCardsFailure")]
    public string ConfirmCardsFailure { get; init; } = "";

    [JsonPropertyName("confirm")] public string Confirm { get; init; } = "";
    [JsonPropertyName("cancel")] public string Cancel { get; init; } = "";
    [JsonPropertyName("dropDownMessage")] public string DropDownMessage { get; init; } = "";

    [JsonPropertyName("declareNatural")] public string DeclareNatural { get; init; } = "";
    [JsonPropertyName("threeFlushes")] public string ThreeFlushes { get; init; } = "";
    [JsonPropertyName("threeStraights")] public string ThreeStraights { get; init; } = "";
    [JsonPropertyName("sixAndAHalfPairs")] public string SixAndAHalfPairs { get; init; } = "";
    [JsonPropertyName("fourTriples")] public string FourTriples { get; init; } = "";
    [JsonPropertyName("fullColored")] public string FullColored { get; init; } = "";
    [JsonPropertyName("allLowHighs")] public string AllLowHighs { get; init; } = "";
    [JsonPropertyName("threeQuads")] public string ThreeQuads { get; init; } = "";

    [JsonPropertyName("threeStraightFlushes")]
    public string ThreeStraightFlushes { get; init; } = "";

    [JsonPropertyName("twelveRoyalties")] public string TwelveRoyalties { get; init; } = "";
    [JsonPropertyName("dragon")] public string Dragon { get; init; } = "";
    [JsonPropertyName("cleanDragon")] public string CleanDragon { get; init; } = "";
    [JsonPropertyName("naturalDeclared")] public string NaturalDeclared { get; init; } = "";
    [JsonPropertyName("naturalHit")] public string NaturalHit { get; init; } = "";
    [JsonPropertyName("none")] public string None { get; init; } = "";
    [JsonPropertyName("onePair")] public string OnePair { get; init; } = "";
    [JsonPropertyName("twoPairs")] public string TwoPairs { get; init; } = "";
    [JsonPropertyName("threeOfAKind")] public string ThreeOfAKind { get; init; } = "";
    [JsonPropertyName("straight")] public string Straight { get; init; } = "";
    [JsonPropertyName("flush")] public string Flush { get; init; } = "";
    [JsonPropertyName("fullHouse")] public string FullHouse { get; init; } = "";
    [JsonPropertyName("fourOfAKind")] public string FourOfAKind { get; init; } = "";
    [JsonPropertyName("straightFlush")] public string StraightFlush { get; init; } = "";
    [JsonPropertyName("misSet")] public string MisSet { get; init; } = "";
    [JsonPropertyName("scoop")] public string Scoop { get; init; } = "";
    [JsonPropertyName("homeRun")] public string HomeRun { get; init; } = "";
    [JsonPropertyName("frontThreeOfAKind")] public string FrontThreeOfAKind { get; init; } = "";
    [JsonPropertyName("middleFullHouse")] public string MiddleFullHouse { get; init; } = "";
    [JsonPropertyName("middleFourOfAKind")] public string MiddleFourOfAKind { get; init; } = "";
    [JsonPropertyName("middleStraightFlush")] public string MiddleStraightFlush { get; init; } = "";
    [JsonPropertyName("backFourOfAKind")] public string BackFourOfAKind { get; init; } = "";
    [JsonPropertyName("backStraightFlush")] public string BackStraightFlush { get; init; } = "";
    [JsonPropertyName("natural")] public string Natural { get; init; } = "";
    [JsonPropertyName("showHelp")] public string ShowHelp { get; init; } = "";
    [JsonPropertyName("about")] public string About { get; init; } = "";
    
    public Dictionary<string, string> AvailableCommands => CommandList;
};