using System.Text.Json;
using BurstBotShared.Shared.Models.Localization.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Localization.Serializables;

namespace BurstBotShared.Shared.Models.Localization;

public record Localization
{
    public string Bot { get; private init; } = null!;
    public BlackJackLocalization BlackJack { get; private init; } = null!;
    public GenericWords GenericWords { get; private init; } = null!;

    public static Localization FromRaw(RawLocalization rawLocalization)
        => new()
        {
            Bot = File.ReadAllText(rawLocalization.Bot),
            BlackJack = JsonSerializer.Deserialize<BlackJackLocalization>(File
                    .ReadAllText(rawLocalization.BlackJack))!
                .LoadCommandHelps(),
            GenericWords = JsonSerializer.Deserialize<GenericWords>(File.ReadAllText(rawLocalization.Generic))!
        };
}