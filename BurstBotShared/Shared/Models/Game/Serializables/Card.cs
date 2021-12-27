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

    public ImmutableArray<int> GetValue()
    {
        var cardValue = (int)Suit + (Number >= 10 ? 10 : Number);
        var values = new List<int> { cardValue };
        if (Number == 1)
        {
            values.Add((int)Suit + 11);
        }

        return values.ToImmutableArray();
    }

    public string ToStringSimple()
    {
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

    public static Card CreateCard(string suit, string rank)
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
}