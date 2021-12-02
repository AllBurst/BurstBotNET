using System.Collections.Immutable;
using BurstBotNET.Shared.Interfaces;
using Newtonsoft.Json;

namespace BurstBotNET.Shared.Models.Game.Serializables;

public record Card : IValueRealizable<ImmutableList<int>>
{
    [JsonProperty("suit")] public Suit Suit { get; init; }
    [JsonProperty("number")] public int Number { get; init; }
    [JsonProperty("is_front")] public bool IsFront { get; init; }

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