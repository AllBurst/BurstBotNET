using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Data.Serializables;

public record UpdateTip
{
    [JsonProperty("adjustment")]
    [JsonPropertyName("adjustment")]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    public TipAdjustment? Adjustment { get; init; }

    [JsonProperty("new_amount")]
    [JsonPropertyName("new_amount")]
    public long NewAmount { get; init; }
};