using System.Collections.Immutable;
using BurstBotNET.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record BlackJackInGameRequest
{
    [JsonProperty("request_type")] 
    [JsonConverter(typeof(StringEnumConverter))]
    public BlackJackInGameRequestType RequestType { get; init; }
    [JsonProperty("game_id")] public string GameId { get; init; } = "";
    [JsonProperty("player_id")] public ulong PlayerId { get; init; }
    [JsonProperty("channel_id")] public ulong ChannelId { get; init; }
    [JsonProperty("player_name")] public string? PlayerName { get; init; }
    [JsonProperty("own_tips")] public long? OwnTips { get; init; }
    
    [JsonProperty("client_type")] 
    [JsonConverter(typeof(StringEnumConverter))]
    public ClientType? ClientType { get; init; }
    [JsonProperty("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonProperty("cards")] public ImmutableList<Card>? Cards { get; init; }
    [JsonProperty("bets")] public int? Bets { get; init; }
};