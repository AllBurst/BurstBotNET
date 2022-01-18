using System.Collections.Immutable;
using System.Drawing;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Objects;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Extensions;

public static class BurstExtensions
{
    private const string SpadeIcon = "<:burst_spade2:930749903158792192>";
    private const string HeartIcon = "<:burst_heart2:930749955914727474>";
    private const string DiamondIcon = "<:burst_diamond2:930749987044851712>";
    private const string ClubIcon = "<:burst_club2:930750022167957504>";

    public static string GetAvatarUrl(this IUser user, ushort size = 1024)
    {
        var num = size is >= 16 and <= 2048
            ? Math.Log(size, 2.0)
            : throw new ArgumentOutOfRangeException(nameof(size));
        if (num < 4.0 || num > 11.0 || num % 1.0 != 0.0)
            throw new ArgumentOutOfRangeException(nameof(size));

        var avatarHash = user.Avatar?.Value ?? "";
        var str1 = !string.IsNullOrWhiteSpace(avatarHash) ? avatarHash.StartsWith("a_") ? "gif" : "png" : "png";
        return !string.IsNullOrWhiteSpace(avatarHash)
            ? $"https://cdn.discordapp.com/avatars/{user.ID.Value}/{avatarHash}.{str1}?size={size}"
            : $"https://cdn.discordapp.com/embed/avatars/{user.Discriminator % 5}.{str1}?size={size}";
    }

    public static string GetAvatarUrl(this IGuildMember member, ushort size = 1024)
        => member.User.Value.GetAvatarUrl(size);

    public static Color ToColor(this BurstColor color)
        => color switch
        {
            BurstColor.CSharp => Color.FromArgb(58, 0, 147),
            BurstColor.Burst => Color.FromArgb(120, 111, 168),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, "Invalid burst color.")
        };

    public static string GetDisplayName(this IGuildMember member)
    {
        var _ = member.Nickname.IsDefined(out var nickname);
        return nickname ?? member.User.Value.Username;
    }

    public static int GetRealizedValues(this IEnumerable<Card> cards, int? rem = null)
    {
        var cardList = cards.ToList();
        var hasAce = cardList.Any(card => card.Number == 1);
        if (hasAce)
        {
            var nonAceValues = cardList.Where(card => card.Number != 1).GetBlackJackValue();
            if (rem.HasValue)
            {
                nonAceValues %= rem.Value;
            }

            cardList
                .Where(card => card.Number == 1)
                .Select(card => card.GetBlackJackValue())
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

        var value = cardList.GetBlackJackValue();
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

    public static Snowflake ToSnowflake(this Suit suit)
        => suit switch
        {
            Suit.Spade => DiscordSnowflake.New(930749903158792192),
            Suit.Heart => DiscordSnowflake.New(930749955914727474),
            Suit.Diamond => DiscordSnowflake.New(930749987044851712),
            Suit.Club => DiscordSnowflake.New(930750022167957504),
            _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Invalid suit.")
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

    public static IEnumerable<T> Intersperse<T>(this IEnumerable<T> array, T padding)
    {
        var newList = new List<T> { padding };
        foreach (var item in array)
        {
            newList.Add(item);
            newList.Add(padding);
        }

        return newList;
    }

    public static List<IMessageComponent> Disable(this Optional<IReadOnlyList<IMessageComponent>> components, bool disableAll, IEnumerable<string>? customIds)
    {
        if (!components.HasValue) return new List<IMessageComponent>();

        var idsToRemove = customIds?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            
        var newComponents = new List<IMessageComponent>(components.Value.Count);
        foreach (var component in components.Value)
        {
            if (component is not ActionRowComponent actionRow) continue;

            var newActionRow = new List<IMessageComponent>(actionRow.Components.Count);

            foreach (var inner in actionRow.Components)
            {
                switch (inner)
                {
                    case ButtonComponent button:
                    {
                        if (!idsToRemove.IsEmpty && idsToRemove.Contains(button.CustomID.Value))
                            newActionRow.Add(button with { IsDisabled = true });
                        else
                            newActionRow.Add(button with { IsDisabled = disableAll ? true : button.IsDisabled });
                        break;
                    }
                    case SelectMenuComponent menu:
                    {
                        if (!idsToRemove.IsEmpty && idsToRemove.Contains(menu.CustomID))
                            newActionRow.Add(menu with { IsDisabled = disableAll ? true : menu.IsDisabled });
                        else
                            newActionRow.Add(menu);
                        break;
                    }
                }
            }

            newComponents.Add(actionRow with { Components = newActionRow });
        }

        return newComponents;
    }

    private static int GetBlackJackValue(this IEnumerable<Card> cards)
    {
        return cards.Sum(card => card.GetBlackJackValue().Max());
    }
}