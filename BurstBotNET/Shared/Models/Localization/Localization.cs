using System.Text.Json;
using BurstBotNET.Shared.Models.Localization.BlackJack.Serializables;
using BurstBotNET.Shared.Models.Localization.Serializables;

namespace BurstBotNET.Shared.Models.Localization;

public record Localization
{
    public string? Bot { get; init; }
    public BlackJackLocalization? BlackJack { get; init; }
    public GenericWords? GenericWords { get; init; }

    public static Localization FromRaw(RawLocalization rawLocalization)
        => new()
        {
            Bot = File.ReadAllText(rawLocalization.Bot),
            BlackJack = JsonSerializer.Deserialize<BlackJackLocalization>(File.ReadAllText(rawLocalization.BlackJack))!,
            GenericWords = JsonSerializer.Deserialize<GenericWords>(File.ReadAllText(rawLocalization.Generic))!
        };
}