using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
public record NinetyNineInGameRequest: IGenericDealData
{
    [JsonPropertyName("request_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NinetyNineInGameRequestType RequestType { get; init; }

    [JsonPropertyName("client_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType? ClientType { get; init; }

    [JsonPropertyName("game_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameType GameType => GameType.NinetyNine;

    [JsonPropertyName("game_id")] public string GameId { get; init; } = null!;
    [JsonPropertyName("player_id")] public ulong PlayerId { get; init; }
    [JsonPropertyName("channel_id")] public ulong ChannelId { get; init; }
    [JsonPropertyName("player_name")] public string? PlayerName { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("play_card")] public ImmutableArray<Card>? PlayCard { get; init; }

    [JsonPropertyName("variation")] public NinetyNineVariation Variation { get; init; }

    [JsonPropertyName("difficulty")] public NinetyNineDifficulty Difficulty { get; init; }

    [JsonPropertyName("direction")] public NinetyNineDirection Variation { get; init; }

}

