using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.OldMaid.Serializables;

public record RawOldMaidPlayerState : IRawState<OldMaidPlayerState, RawOldMaidPlayerState, OldMaidGameProgress>
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

    public static RawOldMaidPlayerState FromState(IState<OldMaidPlayerState, RawOldMaidPlayerState, OldMaidGameProgress> state)
    {
        var playerState = (state as OldMaidPlayerState)!;
        return new RawOldMaidPlayerState
        {
            GameId = playerState.GameId,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName,
            ChannelId = playerState.TextChannel?.ID.Value ?? 0,
            Order = playerState.Order,
            AvatarUrl = playerState.AvatarUrl,
            Cards = playerState.Cards.ToList()
        };
    }

    public async Task<OldMaidPlayerState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var getChannelResult = await guildApi.GetGuildChannelsAsync(guild);
        var channel = !getChannelResult.IsSuccess ? null : getChannelResult.Entity
            .FirstOrDefault(c => c.ID.Value.Equals(ChannelId));
        
        var getMemberResult = await guildApi.GetGuildMemberAsync(guild, DiscordSnowflake.New(PlayerId));
        var member = !getMemberResult.IsSuccess ? null : getMemberResult.Entity;

        return new OldMaidPlayerState
        {
            GameId = GameId,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            TextChannel = channel,
            Order = Order,
            AvatarUrl = member?.GetAvatarUrl() ?? "",
            Cards = Cards.ToImmutableArray()
        };
    }
};