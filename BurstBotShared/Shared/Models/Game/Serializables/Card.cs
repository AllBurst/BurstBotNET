using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.Serializables;

public record Card : IValueRealizable<ImmutableArray<int>>
{
    [JsonPropertyName("suit")]
    [JsonProperty("suit")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public Suit Suit { get; init; }
    
    [JsonPropertyName("number")]
    [JsonProperty("number")]
    public int Number { get; init; }
    
    [JsonPropertyName("is_front")]
    [JsonProperty("is_front")]
    public bool IsFront { get; init; }

    public ImmutableArray<int> GetBlackJackValue()
    {
        var cardValue = (int)Suit + (Number >= 10 ? 10 : Number);
        var values = new List<int> { cardValue };
        if (Number == 1)
        {
            values.Add((int)Suit + 11);
        }

        return values.ToImmutableArray();
    }

    public int GetChinesePokerValue()
        => Number == 1 ? 14 : Number;

    public string ToStringSimple()
    {
        if (Number == 0) return "Joker";
        
        var n = Number switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => Number.ToString()
        };
        return $"{Suit} {n}";
    }

    public override string ToString()
    {
        if (Number == 0) return "🃏 Joker";
        
        var n = Number switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => Number.ToString()
        };
        return $"{Suit.ToSuitPretty()} {n}";
    }

    public string ToSpecifier()
    {
        var suit = Suit switch
        {
            Suit.Spade => "s",
            Suit.Heart => "h",
            Suit.Diamond => "d",
            Suit.Club => "c",
            _ => ""
        };

        var rank = Number switch
        {
            1 => "a",
            11 => "j",
            12 => "q",
            13 => "k",
            _ => Number.ToString()
        };

        return suit + rank;
    }

    public static Card Create(Suit suit, int rank, bool isFront = true)
        => new()
        {
            IsFront = isFront,
            Number = rank,
            Suit = suit
        };

    public static Card Create(string suit, string rank)
    {
        var s = suit switch
        {
            "S" or "s" => Suit.Spade,
            "H" or "h" => Suit.Heart,
            "D" or "d" => Suit.Diamond,
            "C" or "c" => Suit.Club,
            _ => Suit.Club
        };

        var r = rank switch
        {
            "a" or "A" => 1,
            "j" or "J" => 11,
            "q" or "Q" => 12,
            "k" or "K" => 13,
            _ => int.Parse(rank)
        };

        return new Card
        {
            Suit = s,
            IsFront = true,
            Number = r
        };
    }

    public static bool CanCombine(Card card1, Card card2)
    {
        return (card1.Number, card2.Number) switch
        {
            (1, 9) or (9, 1) => true,
            (2, 8) or (8, 2) => true, 
            (3, 7) or (7, 3) => true, 
            (4, 6) or (6, 4) => true, 
            (5, 5) => true, 
            (10, 10) => true, 
            (11, 11) => true, 
            (12, 12) => true, 
            (13, 13) => true, 
            _ => false,
        };
    }
}