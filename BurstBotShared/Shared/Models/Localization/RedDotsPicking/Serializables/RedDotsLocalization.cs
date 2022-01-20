using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;

namespace BurstBotShared.Shared.Models.Localization.RedDotsPicking.Serializables;

public record RedDotsLocalization : ILocalization<RedDotsLocalization>
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

    [JsonPropertyName("cards")] public string Cards { get; init; } = "";
    [JsonPropertyName("use")] public string Use { get; init; } = "";
    [JsonPropertyName("giveUp")] public string GiveUp { get; init; } = "";
    [JsonPropertyName("useMessage")] public string UseMessage { get; init; } = "";
    [JsonPropertyName("giveUpMessage")] public string GiveUpMessage { get; init; } = "";
    [JsonPropertyName("drawMessage")] public string DrawMessage { get; init; } = "";
    [JsonPropertyName("turnMessageTitle")] public string TurnMessageTitle { get; init; } = "";
    [JsonPropertyName("showHelp")] public string ShowHelp { get; init; } = "";
    [JsonPropertyName("cardsOnTable")] public string CardsOnTable { get; init; } = "";
    [JsonPropertyName("forceFive")] public string ForceFive { get; init; } = "";
    [JsonPropertyName("forceGiveUp")] public string ForceGiveUp { get; init; } = "";
    [JsonPropertyName("showGeneral")] public string ShowGeneral { get; init; } = "";
    [JsonPropertyName("showScoring")] public string ShowScoring { get; init; } = "";
    [JsonPropertyName("showFlows")] public string ShowFlows { get; init; } = "";
    [JsonPropertyName("points")] public string Points { get; init; } = "";
    [JsonPropertyName("about")] public string About { get; init; } = "";

    public Dictionary<string, string> AvailableCommands => CommandList;
};