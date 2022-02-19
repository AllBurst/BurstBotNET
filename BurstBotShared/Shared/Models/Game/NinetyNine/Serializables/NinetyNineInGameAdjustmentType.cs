using System.Text.Json.Serialization;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NinetyNineInGameAdjustmentType
{
    Plus,
    Minus,
    One,
    Fourteen,
    Eleven
}
