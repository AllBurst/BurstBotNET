using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Text.Json.Serialization;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
            ChannelId = playerState.TextChannel!.Id,
            Cards = playerState.Cards.ToList(),
            PlayedCards = playerState.PlayedCards,
            Naturals = playerState.Naturals,
            AvatarUrl = playerState.AvatarUrl
        };
    }

    public async Task<ChinesePokerPlayerState> ToState(DiscordGuild guild)
    {
        var channel = guild.GetChannel(ChannelId);
        var member = await guild.GetMemberAsync(PlayerId);
        return new ChinesePokerPlayerState
        {
            AvatarUrl = member.GetAvatarUrl(ImageFormat.Auto),
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