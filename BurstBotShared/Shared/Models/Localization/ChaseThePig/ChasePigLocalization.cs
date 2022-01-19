using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;

namespace BurstBotShared.Shared.Models.Localization.ChaseThePig;

public record ChasePigLocalization : ILocalization<ChasePigLocalization>
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
    [JsonPropertyName("expose")] public string Expose { get; init; } = "";
    [JsonPropertyName("noExpose")] public string NoExpose { get; init; } = "";

    [JsonPropertyName("noExposableCardsSecond")]
    public string NoExposableCardsSecond { get; init; } = "";
    
    [JsonPropertyName("noExposableCardsThird")]
    public string NoExposableCardsThird { get; init; } = "";
    
    [JsonPropertyName("play")] public string Play { get; init; } = "";
    [JsonPropertyName("playMessage")] public string PlayMessage { get; init; } = "";
    [JsonPropertyName("winTurnMessage")] public string WinTurnMessage { get; init; } = "";
    [JsonPropertyName("exposeTitle")] public string ExposeTitle { get; init; } = "";
    [JsonPropertyName("exposeMessage")] public string ExposeMessage { get; init; } = "";
    [JsonPropertyName("noExposeMessage")] public string NoExposeMessage { get; init; } = "";
    [JsonPropertyName("exposeMessageFirst")] public string ExposeMessageFirst { get; init; } = "";
    [JsonPropertyName("turnMessageTitle")] public string TurnMessageTitle { get; init; } = "";
    [JsonPropertyName("showHelp")] public string ShowHelp { get; init; } = "";
    [JsonPropertyName("exposeHeartA")] public string ExposeHeartA { get; init; } = "";
    [JsonPropertyName("exposePig")] public string ExposePig { get; init; } = "";
    [JsonPropertyName("exposeGoat")] public string ExposeGoat { get; init; } = "";
    [JsonPropertyName("exposeTransformer")] public string ExposeTransformer { get; init; } = "";
    [JsonPropertyName("showGeneral")] public string ShowGeneral { get; init; } = "";
    [JsonPropertyName("showScoring")] public string ShowScoring { get; init; } = "";
    [JsonPropertyName("showFlows")] public string ShowFlows { get; init; } = "";
    [JsonPropertyName("showExposure")] public string ShowExposure { get; init; } = "";
    [JsonPropertyName("points")] public string Points { get; init; } = "";
    [JsonPropertyName("about")] public string About { get; init; } = "";

    public Dictionary<string, string> AvailableCommands => CommandList;
};