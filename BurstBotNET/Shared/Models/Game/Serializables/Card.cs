using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotNET.Shared.Interfaces;

namespace BurstBotNET.Shared.Models.Game.Serializables;

public record Card : IValueRealizable<ImmutableList<int>>
{
    [JsonPropertyName("suit")] public Suit Suit { get; init; }
    [JsonPropertyName("number")] public int Number { get; init; }
    [JsonPropertyName("is_front")] public bool IsFront { get; init; }

    public ImmutableList<int> GetValue()
    {
        var cardValue = (int)Suit + (Number >= 10 ? 10 : Number);
        var values = new List<int> { cardValue };
        if (Number == 1)
        {
            values.Add((int)Suit + 11);
        }

        return values.ToImmutableList();
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
        return $"{Suit} {n}";
    }
}