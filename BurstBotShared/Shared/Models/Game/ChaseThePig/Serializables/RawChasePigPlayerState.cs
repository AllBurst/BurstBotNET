using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;

public record RawChasePigPlayerState : IRawState<ChasePigPlayerState, RawChasePigPlayerState, ChasePigGameProgress>, IRawPlayerState
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

    [JsonPropertyName("avatar_url")]
    [JsonProperty("avatar_url")]
    public string AvatarUrl { get; init; } = "";
    
    [JsonPropertyName("scores")]
    [JsonProperty("scores")]
    public int Scores { get; init; }

    [JsonPropertyName("collected_cards")]
    [JsonProperty("collected_cards")]
    public List<Card> CollectedCards { get; init; } = new();
    
    [JsonPropertyName("order")]
    [JsonProperty("order")]
    public int Order { get; init; }

    public static RawChasePigPlayerState FromState(IState<ChasePigPlayerState, RawChasePigPlayerState, ChasePigGameProgress> state)
    {
        var playerState = (state as ChasePigPlayerState)!;
        return new RawChasePigPlayerState
        {
            AvatarUrl = playerState.AvatarUrl,
            Cards = playerState.Cards.ToList(),
            ChannelId = playerState.TextChannel!.ID.Value,
            CollectedCards = playerState.CollectedCards.ToList(),
            GameId = playerState.GameId,
            Order = playerState.Order,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName,
            Scores = playerState.Scores
        };
    }

    public async Task<ChasePigPlayerState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var getChannelResult = await guildApi.GetGuildChannelsAsync(guild);
        var channel = !getChannelResult.IsSuccess ? null : getChannelResult.Entity
            .FirstOrDefault(c => c.ID.Value.Equals(ChannelId));
        
        var getMemberResult = await guildApi.GetGuildMemberAsync(guild, DiscordSnowflake.New(PlayerId));
        var member = !getMemberResult.IsSuccess ? null : getMemberResult.Entity;

        return new ChasePigPlayerState
        {
            AvatarUrl = member?.GetAvatarUrl() ?? "",
            Cards = Cards.ToImmutableArray(),
            CollectedCards = CollectedCards.ToImmutableArray(),
            GameId = GameId,
            Order = Order,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            Scores = Scores,
            TextChannel = channel,
        };
    }
};