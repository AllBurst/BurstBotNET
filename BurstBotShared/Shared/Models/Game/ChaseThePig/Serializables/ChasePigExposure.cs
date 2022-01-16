using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;

[JsonConverter(typeof(JsonStringEnumConverter))]
[Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
public enum ChasePigExposure
{
    [EnumMember(Value = "DoubleMinus")]
    DoubleMinus,
    [EnumMember(Value = "DoublePig")]
    DoublePig,
    [EnumMember(Value = "DoubleGoat")]
    DoubleGoat,
    [EnumMember(Value = "Transformer")]
    Transformer,
    [EnumMember(Value = "FirstDoubleMinus")]
    FirstDoubleMinus,
    [EnumMember(Value = "FirstDoublePig")]
    FirstDoublePig,
    [EnumMember(Value = "FirstDoubleGoat")]
    FirstDoubleGoat,
    [EnumMember(Value = "FirstTransformer")]
    FirstTransformer,
}