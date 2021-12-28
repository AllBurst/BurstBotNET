#pragma warning disable CA2252
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using Newtonsoft.Json;

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
            ChannelId = playerState.TextChannel?.Id ?? 0,
            GameId = playerState.GameId,
            Order = playerState.Order,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName
        };
    }

    public async Task<NinetyNinePlayerState> ToState(DiscordGuild guild)
    {
        var channel = guild.GetChannel(ChannelId);
        var member = await guild.GetMemberAsync(PlayerId);
        return new NinetyNinePlayerState
        {
            AvatarUrl = member.GetAvatarUrl(ImageFormat.Auto),
            Cards = Cards.ToImmutableArray(),
            GameId = GameId,
            Order = Order,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            TextChannel = channel
        };
    }
};