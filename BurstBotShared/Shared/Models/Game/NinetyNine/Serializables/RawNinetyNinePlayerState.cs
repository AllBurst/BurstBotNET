#pragma warning disable CA2252
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;

public record RawNinetyNinePlayerState : IRawState<NinetyNinePlayerState, RawNinetyNinePlayerState, NinetyNineGameProgress>
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
    
    [JsonPropertyName("order")]
    [JsonProperty("order")]
    public int Order { get; init; }

    [JsonPropertyName("cards")]
    [JsonProperty("cards")]
    public List<Card> Cards { get; init; } = new();

    [JsonPropertyName("avatar_url")]
    [JsonProperty("avatar_url")]
    public string AvatarUrl { get; init; } = "";

    [Pure]
    public static RawNinetyNinePlayerState FromState(IState<NinetyNinePlayerState, RawNinetyNinePlayerState, NinetyNineGameProgress> state)
    {
        var playerState = state as NinetyNinePlayerState;
        return new RawNinetyNinePlayerState
        {
            AvatarUrl = playerState!.AvatarUrl,
            Cards = playerState.Cards.ToList(),
            ChannelId = playerState.TextChannel?.ID.Value ?? 0,
            GameId = playerState.GameId,
            Order = playerState.Order,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName
        };
    }

    public async Task<NinetyNinePlayerState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var getChannelResult = await guildApi.GetGuildChannelsAsync(guild);
        var channel = !getChannelResult.IsSuccess ? null : getChannelResult.Entity
            .FirstOrDefault(c => c.ID.Value.Equals(ChannelId));
        
        var getMemberResult = await guildApi.GetGuildMemberAsync(guild, DiscordSnowflake.New(PlayerId));
        var member = !getMemberResult.IsSuccess ? null : getMemberResult.Entity;
        
        return new NinetyNinePlayerState
        {
            AvatarUrl = member?.GetAvatarUrl() ?? "",
            Cards = Cards.ToImmutableArray(),
            GameId = GameId,
            Order = Order,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            TextChannel = channel
        };
    }
};