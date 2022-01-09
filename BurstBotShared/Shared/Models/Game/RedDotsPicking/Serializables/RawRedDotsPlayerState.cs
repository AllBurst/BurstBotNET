using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using Newtonsoft.Json;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Models.Game.RedDotsPicking.Serializables;

public record RawRedDotsPlayerState : IRawState<RedDotsPlayerState, RawRedDotsPlayerState, RedDotsGameProgress>
{
    [JsonPropertyName("game_id")]
    [JsonProperty("game_id")]
    public string GameId { get; init; } = null!;
    
    [JsonPropertyName("player_id")]
    [JsonProperty("player_id")]
    public ulong PlayerId { get; init; }
    
    [JsonPropertyName("player_name")]
    [JsonProperty("player_name")]
    public string PlayerName { get; init; } = null!;
    
    [JsonPropertyName("channel_id")]
    [JsonProperty("channel_id")]
    public ulong ChannelId { get; init; }
    
    [JsonPropertyName("avatar_url")]
    [JsonProperty("avatar_url")]
    public string AvatarUrl { get; init; } = null!;
    
    [JsonPropertyName("order")]
    [JsonProperty("order")]
    public int Order { get; init; }
    
    [JsonPropertyName("cards")]
    [JsonProperty("cards")]
    public List<Card> Cards { get; init; } = new();
    
    [JsonPropertyName("collected_cards")]
    [JsonProperty("collected_cards")]
    public List<Card> CollectedCards { get; init; } = new();
    
    [JsonPropertyName("score")]
    [JsonProperty("score")]
    public int Score { get; init; }
    
    [JsonPropertyName("score_adjustment")]
    [JsonProperty("score_adjustment")]
    public int ScoreAdjustment { get; init; }
    
    [JsonPropertyName("second_move")]
    [JsonProperty("second_move")]
    public bool SecondMove { get; init; }
    
    [JsonPropertyName("points")]
    [JsonProperty("points")]
    public int Points { get; init; }
    
    public static RawRedDotsPlayerState FromState(IState<RedDotsPlayerState, RawRedDotsPlayerState, RedDotsGameProgress> state)
    {
        var playerState = (state as RedDotsPlayerState)!;
        return new RawRedDotsPlayerState
        {
            AvatarUrl = playerState.AvatarUrl,
            Cards = playerState.Cards.ToList(),
            ChannelId = playerState.TextChannel!.ID.Value,
            CollectedCards = playerState.CollectedCards.ToList(),
            GameId = playerState.GameId,
            Order = playerState.Order,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName,
            Points = playerState.Points,
            Score = playerState.Score,
            ScoreAdjustment = playerState.ScoreAdjustment,
            SecondMove = playerState.SecondMove
        };
    }

    public async Task<RedDotsPlayerState> ToState(IDiscordRestGuildAPI guildApi, Snowflake guild)
    {
        var getChannelResult = await guildApi.GetGuildChannelsAsync(guild);
        var channel = !getChannelResult.IsSuccess ? null : getChannelResult.Entity
            .FirstOrDefault(c => c.ID.Value.Equals(ChannelId));
        
        var getMemberResult = await guildApi.GetGuildMemberAsync(guild, DiscordSnowflake.New(PlayerId));
        var member = !getMemberResult.IsSuccess ? null : getMemberResult.Entity;

        return new RedDotsPlayerState
        {
            AvatarUrl = member?.GetAvatarUrl() ?? "",
            Cards = Cards.ToImmutableArray(),
            CollectedCards = CollectedCards.ToImmutableArray(),
            GameId = GameId,
            Order = Order,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            Points = Points,
            Score = Score,
            ScoreAdjustment = ScoreAdjustment,
            TextChannel = channel,
            SecondMove = SecondMove
        };
    }
};