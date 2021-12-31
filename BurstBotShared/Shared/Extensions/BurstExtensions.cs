using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Extensions;

public static class BurstExtensions
{
    private const string SpadeIcon = "<:burst_spade:910826637657010226>";
    private const string HeartIcon = "<:burst_heart:910826529511051284>";
    private const string DiamondIcon = "<:burst_diamond:910826609576140821>";
    private const string ClubIcon = "<:burst_club:910826578336948234>";
    
    public static int GetValue(this IEnumerable<Card> cards)
    {
        return cards.Sum(card => card.GetValue().Max());
    }

    public static int GetRealizedValues(this IEnumerable<Card> cards, int? rem = null)
    {
        var cardList = cards.ToList();
        var hasAce = cardList.Any(card => card.Number == 1);
        if (hasAce)
        {
            var nonAceValues = cardList.Where(card => card.Number != 1).GetValue();
            if (rem.HasValue)
            {
                nonAceValues %= rem.Value;
            }

            cardList
                .Where(card => card.Number == 1)
                .Select(card => card.GetValue())
                .Select(values => values.Select(v =>
                {
                    if (rem.HasValue)
                    {
                        return v % rem.Value;
                    }

                    return v;
                }).ToImmutableArray())
                .ToImmutableList()
                .ForEach(values =>
                {
                    var max = values.Max();
                    var min = values.Min();
                    nonAceValues += nonAceValues + max > 21 ? min : max;
                });
            return nonAceValues;
        }

        var value = cardList.GetValue();
        return rem.HasValue ? value % rem.Value : value;
    }

    public static string ToSuitPretty(this Suit suit)
        => suit switch
        {
            Suit.Spade => $"{SpadeIcon} {suit}",
            Suit.Heart => $"{HeartIcon} {suit}",
            Suit.Diamond => $"{DiamondIcon} {suit}",
            Suit.Club => $"{ClubIcon} {suit}",
            _ => ""
        };

    public static DiscordComponentEmoji ToComponentEmoji(this Suit suit)
        => suit switch
        {
            Suit.Spade => new DiscordComponentEmoji(910826637657010226),
            Suit.Heart => new DiscordComponentEmoji(910826529511051284),
            Suit.Diamond => new DiscordComponentEmoji(910826609576140821),
            Suit.Club => new DiscordComponentEmoji(910826578336948234),
            _ => new DiscordComponentEmoji("❓")
        };

    public static string ToLocalizedString(this ChinesePokerNatural natural, ChinesePokerLocalization localization)
        => natural switch
        {
            ChinesePokerNatural.ThreeFlushes => localization.ThreeFlushes,
            ChinesePokerNatural.ThreeStraights => localization.ThreeStraights,
            ChinesePokerNatural.SixAndAHalfPairs => localization.SixAndAHalfPairs,
            ChinesePokerNatural.FourTriples => localization.FourTriples,
            ChinesePokerNatural.FullColored => localization.FullColored,
            ChinesePokerNatural.AllLowHighs => localization.AllLowHighs,
            ChinesePokerNatural.ThreeQuads => localization.ThreeQuads,
            ChinesePokerNatural.ThreeStraightFlushes => localization.ThreeStraightFlushes,
            ChinesePokerNatural.TwelveRoyalties => localization.TwelveRoyalties,
            ChinesePokerNatural.Dragon => localization.Dragon,
            ChinesePokerNatural.CleanDragon => localization.CleanDragon,
            _ => throw new ArgumentOutOfRangeException(nameof(natural), natural, "Invalid natural.")
        };

    public static string ToLocalizedString(this ChinesePokerCombinationType combinationType,
        ChinesePokerLocalization localization)
        => combinationType switch
        {
            ChinesePokerCombinationType.None => localization.None,
            ChinesePokerCombinationType.OnePair => localization.OnePair,
            ChinesePokerCombinationType.TwoPairs => localization.TwoPairs,
            ChinesePokerCombinationType.ThreeOfAKind => localization.ThreeOfAKind,
            ChinesePokerCombinationType.Straight => localization.Straight,
            ChinesePokerCombinationType.Flush => localization.Flush,
            ChinesePokerCombinationType.FullHouse => localization.FullHouse,
            ChinesePokerCombinationType.FourOfAKind => localization.FourOfAKind,
            ChinesePokerCombinationType.StraightFlush => localization.StraightFlush,
            _ => throw new ArgumentOutOfRangeException(nameof(combinationType), combinationType, "Invalid combination.")
        };

    public static string ToLocalizedString(this ChinesePokerInGameResponseRewardType rewardType,
        ChinesePokerLocalization localization)
        => rewardType switch
        {
            ChinesePokerInGameResponseRewardType.MisSet => localization.MisSet,
            ChinesePokerInGameResponseRewardType.Scoop => localization.Scoop,
            ChinesePokerInGameResponseRewardType.HomeRun => localization.HomeRun,
            ChinesePokerInGameResponseRewardType.FrontThreeOfAKind => localization.FrontThreeOfAKind,
            ChinesePokerInGameResponseRewardType.MiddleFullHouse => localization.MiddleFullHouse,
            ChinesePokerInGameResponseRewardType.MiddleFourOfAKind => localization.MiddleFourOfAKind,
            ChinesePokerInGameResponseRewardType.MiddleStraightFlush => localization.MiddleStraightFlush,
            ChinesePokerInGameResponseRewardType.BackFourOfAKind => localization.BackFourOfAKind,
            ChinesePokerInGameResponseRewardType.BackStraightFlush => localization.BackStraightFlush,
            ChinesePokerInGameResponseRewardType.Natural => localization.Natural,
            _ => throw new ArgumentOutOfRangeException(nameof(rewardType), rewardType, "Invalid reward type.")
        };
}