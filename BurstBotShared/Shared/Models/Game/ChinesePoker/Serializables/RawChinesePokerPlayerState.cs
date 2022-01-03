using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;

public record
    RawChinesePokerPlayerState : IRawState<ChinesePokerPlayerState, RawChinesePokerPlayerState,
        ChinesePokerGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = "";

    [JsonPropertyName("player_id")]
    [JsonProperty("player_id")]
    public ulong PlayerId { get; init; }

    [JsonPropertyName("player_name")]
    [JsonProperty("player_name")]
    public string PlayerName { get; init; } = "";

    [JsonPropertyName("channel_id")]
    [JsonProperty("channel_id")]
    public ulong ChannelId { get; init; }

    [JsonPropertyName("cards")]
    [JsonProperty("cards")]
    public List<Card> Cards { get; init; } = new();

    [JsonPropertyName("played_cards")]
    [JsonProperty("played_cards")]
    public Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> PlayedCards { get; init; } = new();

    [JsonPropertyName("naturals")]
    [JsonProperty("naturals")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public ChinesePokerNatural? Naturals { get; init; }

    [JsonPropertyName("avatar_url")]
    [JsonProperty("avatar_url")]
    public string AvatarUrl { get; init; } = "";

    [Pure]
    public static RawChinesePokerPlayerState FromState(
        IState<ChinesePokerPlayerState, RawChinesePokerPlayerState, ChinesePokerGameProgress> state)
    {
        var playerState = state as ChinesePokerPlayerState;
        return new RawChinesePokerPlayerState
        {
            GameId = playerState!.GameId,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName,
            ChannelId = playerState.TextChannel!.ID.Value,
            Cards = playerState.Cards.ToList(),
            PlayedCards = playerState.PlayedCards,
            Naturals = playerState.Naturals,
            AvatarUrl = playerState.AvatarUrl
        };
    }

    public async Task<ChinesePokerPlayerState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var getChannelResult = await guildApi.GetGuildChannelsAsync(guild);
        var channel = !getChannelResult.IsSuccess ? null : getChannelResult.Entity
            .FirstOrDefault(c => c.ID.Value.Equals(ChannelId));
        
        var getMemberResult = await guildApi.GetGuildMemberAsync(guild, DiscordSnowflake.New(PlayerId));
        var member = !getMemberResult.IsSuccess ? null : getMemberResult.Entity;
        
        return new ChinesePokerPlayerState
        {
            AvatarUrl = member?.GetAvatarUrl() ?? "",
            GameId = GameId,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            TextChannel = channel,
            Cards = Cards.ToImmutableArray(),
            PlayedCards = PlayedCards,
            Naturals = Naturals
        };
    }
}