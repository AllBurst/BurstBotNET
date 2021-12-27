using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using Newtonsoft.Json;

namespace BurstBotShared.Shared.Models.Game.BlackJack.Serializables;

public record RawBlackJackPlayerState : IRawState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>
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
    
    [JsonPropertyName("own_tips")] 
    [JsonProperty("own_tips")] 
    public long OwnTips { get; init; }
    
    [JsonPropertyName("bet_tips")] 
    [JsonProperty("bet_tips")] 
    public int BetTips { get; init; }
    
    [JsonPropertyName("avatar_url")] 
    [JsonProperty("avatar_url")] 
    public string AvatarUrl { get; init; } = "";
    
    [JsonPropertyName("order")] 
    [JsonProperty("order")] 
    public int Order { get; init; }
    
    [JsonPropertyName("cards")] 
    [JsonProperty("cards")] 
    public List<Card> Cards { get; init; } = new();
    
    public static RawBlackJackPlayerState FromState(IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress> state)
    {
        var playerState = state as BlackJackPlayerState;
        return new RawBlackJackPlayerState
        {
            AvatarUrl = playerState!.AvatarUrl,
            BetTips = playerState.BetTips,
            Cards = playerState.Cards.ToList(),
            ChannelId = playerState.TextChannel?.Id ?? 0,
            GameId = playerState.GameId,
            Order = playerState.Order,
            OwnTips = playerState.OwnTips,
            PlayerId = playerState.PlayerId,
            PlayerName = playerState.PlayerName
        };
    }

    public async Task<BlackJackPlayerState> ToState(DiscordGuild guild)
    {
        var channel = guild.GetChannel(ChannelId);
        var member = await guild.GetMemberAsync(PlayerId);
        return new BlackJackPlayerState
        {
            AvatarUrl = member.GetAvatarUrl(ImageFormat.Auto),
            BetTips = BetTips,
            Cards = Cards,
            GameId = GameId,
            Order = Order,
            OwnTips = OwnTips,
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            TextChannel = channel
        };
    }
};