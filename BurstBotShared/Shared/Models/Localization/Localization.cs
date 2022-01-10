using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Localization.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Localization.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Localization.RedDotsPicking.Serializables;
using BurstBotShared.Shared.Models.Localization.Serializables;

namespace BurstBotShared.Shared.Models.Localization;

public record Localization
{
    public string Bot { get; private init; } = null!;
    public BlackJackLocalization BlackJack { get; private init; } = null!;
    public ChinesePokerLocalization ChinesePoker { get; private init; } = null!;
    public OldMaidLocalization OldMaid { get; private init; } = null!;
    public RedDotsLocalization RedDotsPicking { get; private init; } = null!;
    public GenericWords GenericWords { get; private init; } = null!;

    public static Localization FromRaw(RawLocalization rawLocalization)
    {
        return new Localization
        {
            Bot = File.ReadAllText(rawLocalization.Bot),
            BlackJack = ((ILocalization<BlackJackLocalization>)JsonSerializer.Deserialize<BlackJackLocalization>(File
                    .ReadAllText(rawLocalization.BlackJack))!)
                .LoadCommandHelps(),
            ChinesePoker =
                ((ILocalization<ChinesePokerLocalization>)JsonSerializer.Deserialize<ChinesePokerLocalization>(File
                    .ReadAllText(rawLocalization.ChinesePoker))!).LoadCommandHelps(),
            OldMaid = ((ILocalization<OldMaidLocalization>)JsonSerializer.Deserialize<OldMaidLocalization>(File
                .ReadAllText(rawLocalization.OldMaid))!).LoadCommandHelps(),
            RedDotsPicking = ((ILocalization<RedDotsLocalization>)JsonSerializer.Deserialize<RedDotsLocalization>(File
                .ReadAllText(rawLocalization.RedDotsPicking))!).LoadCommandHelps(),
            GenericWords = JsonSerializer.Deserialize<GenericWords>(File.ReadAllText(rawLocalization.Generic))!
        };
    }
}