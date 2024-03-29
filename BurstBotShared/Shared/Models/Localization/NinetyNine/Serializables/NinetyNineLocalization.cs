﻿using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;

namespace BurstBotShared.Shared.Models.Localization.NinetyNine.Serializables;
public record NinetyNineLocalization : ILocalization<NinetyNineLocalization>
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
    [JsonPropertyName("play")] public string Play { get; init; } = "";
    [JsonPropertyName("playMessage")] public string PlayMessage { get; init; } = "";
    [JsonPropertyName("burstMessage")] public string BurstMessage { get; init; } = "";

    [JsonPropertyName("turnMessageTitle")] public string TurnMessageTitle { get; init; } = "";
    [JsonPropertyName("showHelp")] public string ShowHelp { get; init; } = "";
    [JsonPropertyName("plus")] public string Plus { get; init; } = "";
    [JsonPropertyName("plusSpecific")] public string PlusSpecific { get; init; } = "";
    [JsonPropertyName("minus")] public string Minus { get; init; } = "";
    [JsonPropertyName("confirm")] public string Confirm { get; init; } = "";
    [JsonPropertyName("gameOver")] public string GameOver { get; init; } = "";
    [JsonPropertyName("selectPlayerMessage")] public string SelectPlayerMessage { get; init; } = "";
    [JsonPropertyName("plusOrMinusMessage")] public string PlusOrMinusMessage { get; init; } = "";
    [JsonPropertyName("currentTotal")] public string CurrentTotal { get; init; } = "";
    [JsonPropertyName("notOnlyQueen")] public string NotOnlyQueen { get; init; } = "";

    [JsonPropertyName("plusOneOrFourteen")]
    public string PlusOneOrFourteen { get; init; } = "";

    [JsonPropertyName("plusOneOrEleven")] public string PlusOneOrEleven { get; init; } = "";
    [JsonPropertyName("taiwanese")] public string Taiwanese { get; init; } = "";
    [JsonPropertyName("icelandic")] public string Icelandic { get; init; } = "";
    [JsonPropertyName("standard")] public string Standard { get; init; } = "";
    [JsonPropertyName("bloody")] public string Bloody { get; init; } = "";
    public Dictionary<string, string> AvailableCommands => CommandList;
};
