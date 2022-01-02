using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;

namespace BurstBotShared.Shared.Models.Localization.BlackJack.Serializables;

public record BlackJackLocalization : ILocalization<BlackJackLocalization>
{
    [JsonPropertyName("commandList")] public Dictionary<string, string> CommandList { get; init; } = new();

    [JsonPropertyName("initialMessagePrefix")]
    public string InitialMessagePrefix { get; init; } = "";

    [JsonPropertyName("initialMessagePostfix")]
    public string InitialMessagePostfix { get; init; } = "";

    [JsonPropertyName("initialMessageDescription")]
    public string InitialMessageDescription { get; init; } = "";

    [JsonPropertyName("initialMessageTitle")]
    public string InitialMessageTitle { get; init; } = "";

    [JsonPropertyName("initialMessageFooter")]
    public string InitialMessageFooter { get; init; } = "";

    [JsonPropertyName("drawMessage")] public string DrawMessage { get; init; } = "";
    [JsonPropertyName("cardPoints")] public string CardPoints { get; init; } = "";
    [JsonPropertyName("standMessage")] public string StandMessage { get; init; } = "";
    [JsonPropertyName("raiseMessage")] public string RaiseMessage { get; init; } = "";
    [JsonPropertyName("unknownMessage")] public string UnknownMessage { get; init; } = "";

    [JsonPropertyName("raiseExcessiveNumber")]
    public string RaiseExcessiveNumber { get; init; } = "";

    [JsonPropertyName("raiseInvalidNumber")]
    public string RaiseInvalidNumber { get; init; } = "";

    [JsonPropertyName("callMessage")] public string CallMessage { get; init; } = "";
    [JsonPropertyName("foldMessage")] public string FoldMessage { get; init; } = "";
    [JsonPropertyName("allinMessage")] public string AllinMessage { get; init; } = "";
    [JsonPropertyName("timeout")] public string Timeout { get; init; } = "";
    [JsonPropertyName("turnMessageTitle")] public string TurnMessageTitle { get; init; } = "";

    [JsonPropertyName("progressingFooter")]
    public string ProgressingFooter { get; init; } = "";

    [JsonPropertyName("turnMessageDescription")]
    public string TurnMessageDescription { get; init; } = "";

    [JsonPropertyName("highestBets")] public string HighestBets { get; init; } = "";
    [JsonPropertyName("yourBets")] public string YourBets { get; init; } = "";
    [JsonPropertyName("tipsBeforeGame")] public string TipsBeforeGame { get; init; } = "";

    [JsonPropertyName("gamblingInitialMessage")]
    public string GamblingInitialMessage { get; init; } = "";

    [JsonPropertyName("winTitle")] public string WinTitle { get; init; } = "";
    [JsonPropertyName("winDescription")] public string WinDescription { get; init; } = "";

    [JsonPropertyName("totalPointsMessage")]
    public string TotalPointsMessage { get; init; } = "";

    [JsonPropertyName("draw")] public string Draw { get; init; } = "";
    [JsonPropertyName("stand")] public string Stand { get; init; } = "";
    [JsonPropertyName("call")] public string Call { get; init; } = "";
    [JsonPropertyName("fold")] public string Fold { get; init; } = "";
    [JsonPropertyName("raise")] public string Raise { get; init; } = "";
    [JsonPropertyName("allin")] public string AllIn { get; init; } = "";
    [JsonPropertyName("raisePrompt")] public string RaisePrompt { get; init; } = "";
    [JsonPropertyName("rules")] public string Rules { get; init; } = "";
    [JsonPropertyName("gameFlow")] public string GameFlow { get; init; } = "";
    [JsonPropertyName("showHelp")] public string ShowHelp { get; init; } = "";

    [JsonIgnore] public Dictionary<string, string> AvailableCommands => CommandList;
}