using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using DSharpPlus.Entities;

namespace BurstBotNET.Shared.Models.Game.BlackJack.Serializables;

public record RawBlackJackGameState
{
    [JsonPropertyName("game_id")] public string GameId { get; init; } = "";
    [JsonPropertyName("last_active_time")] public string LastActiveTime { get; init; } = "";
    [JsonPropertyName("players")] public Dictionary<long, RawBlackJackPlayerState> Players { get; init; } = new();
    [JsonPropertyName("progress")] public BlackJackGameProgress Progress { get; init; }
    [JsonPropertyName("current_player_order")] public int CurrentPlayerOrder { get; init; }
    [JsonPropertyName("highest_bet")] public int HighestBet { get; init; }
    [JsonPropertyName("current_turn")] public int CurrentTurn { get; init; }
    [JsonPropertyName("previous_player_id")] public long PreviousPlayerId { get; init; }

    [JsonPropertyName("previous_request_type")]
    public string PreviousRequestType { get; init; } = "";

    public static RawBlackJackGameState FromGameState(BlackJackGameState state)
        => new()
        {
            CurrentPlayerOrder = state.CurrentPlayerOrder,
            CurrentTurn = state.CurrentTurn,
            GameId = state.GameId,
            HighestBet = state.HighestBet,
            LastActiveTime = state.LastActiveTime.ToString(CultureInfo.InvariantCulture),
            Players = state.Players.ToDictionary(pair => pair.Key, pair => pair.Value.ToRaw()),
            PreviousPlayerId = state.PreviousPlayerId,
            PreviousRequestType = state.PreviousRequestType,
            Progress = state.Progress
        };

    public async Task<BlackJackGameState> ToGameState(DiscordGuild guild)
    {
        var players = Players
            .AsEnumerable()
            .Select(async pair =>
            {
                var (key, value) = pair;
                var playerState = await value.ToPlayerState(guild);
                return KeyValuePair.Create(key, playerState);
            })
            .ToList();

        var playerList = new List<KeyValuePair<long, BlackJackPlayerState>>();
        foreach (var task in players)
        {
            var pair = await task;
            playerList.Add(pair);
        }

        return new BlackJackGameState
        {
            CurrentPlayerOrder = CurrentPlayerOrder,
            CurrentTurn = CurrentTurn,
            GameId = GameId,
            HighestBet = HighestBet,
            LastActiveTime = DateTime.Parse(LastActiveTime, CultureInfo.InvariantCulture),
            Players = new ConcurrentDictionary<long, BlackJackPlayerState>(playerList),
            PreviousPlayerId = PreviousPlayerId,
            PreviousRequestType = PreviousRequestType,
            Progress = Progress
        };
    }
};