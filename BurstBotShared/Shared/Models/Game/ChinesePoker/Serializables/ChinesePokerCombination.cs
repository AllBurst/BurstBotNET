using System.Text.Json.Serialization;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record ChinesePokerCombination
{
    [JsonPropertyName("combination_type")]
    [JsonProperty("combination_type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public ChinesePokerCombinationType CombinationType { get; init; }
    
    [JsonPropertyName("cards")]
    [JsonProperty("cards")]
    public List<Card> Cards { get; init; } = new();
};