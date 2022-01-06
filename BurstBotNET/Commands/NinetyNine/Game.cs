#pragma warning disable CA2252
using BurstBotShared.Services;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Extensions.Embeds;
using Remora.Rest.Core;
using Remora.Rest.Results;
using Channel = System.Threading.Channels.Channel;
using Constants = BurstBotShared.Shared.Constants;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.NinetyNine;

using NinetyNineGame = IGame<NinetyNineGameState, RawNinetyNineGameState, NinetyNine, NinetyNinePlayerState, NinetyNineGameProgress, NinetyNineInGameRequestType>;
public partial class NinetyNine : NinetyNineGame
{
    private static readonly ImmutableArray<string> InGameRequestTypes =
    Enum.GetNames<NinetyNineInGameRequestType>()
        .ToImmutableArray();

    public static Task<bool> HandleProgress(string messageContent, NinetyNineGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, NinetyNineGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static async Task AddPlayerState(string gameId, Snowflake guild, NinetyNinePlayerState playerState, GameStates gameStates)
    {
        var state = gameStates
           .NinetyNineGameStates
           .Item1
           .GetOrAdd(gameId, new NinetyNineGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel.ID.Value,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName
            })));
    }

    public static async Task StartListening(string gameId, State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var gameState = state.GameStates.NinetyNineGameStates.Item1
            .GetOrAdd(gameId, new NinetyNineGameState());
        logger.LogDebug("Chinese Poker game progress: {Progress}", gameState.Progress);

        await gameState.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        if (gameState.Progress != NinetyNineGameProgress.NotAvailable)
        {
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }

        gameState.Progress = NinetyNineGameProgress.Starting;
        gameState.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");

        var buffer = ArrayPool<byte>.Create(Constants.BufferSize, 1024);

        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession =
            await Game.GenericOpenWebSocketSession(GameName, state.Config, logger, cancellationTokenSource);
        gameState.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");

        var timeout = state.Config.Timeout;
        while (!gameState.Progress.Equals(NinetyNineGameProgress.Closed))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = NinetyNineGame.RunChannelTask(socketSession, gameState, InGameRequestTypes,
                NinetyNineInGameRequestType.Close, NinetyNineGameProgress.Closed, logger, cancellationTokenSource);

            var broadcastTask = NinetyNineGame.RunBroadcastTask(socketSession, gameState, buffer, state,
                channelApi,
                guildApi,
                logger, cancellationTokenSource);

            var timeoutTask = NinetyNineGame.RunTimeoutTask(timeout, gameState, NinetyNineGameProgress.Closed,
                logger,
                timeoutCancellationTokenSource);

            await await Task.WhenAny(channelTask, broadcastTask, timeoutTask);
            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
                logger.LogDebug("Timeout task cancelled");
                timeoutCancellationTokenSource.Dispose();
            });
        }

        await Game.GenericCloseGame(socketSession, logger, cancellationTokenSource);
        var retrieveResult =
            state.GameStates.ChinesePokerGameStates.Item1.TryGetValue(gameState.GameId, out var retrievedState);
        if (!retrieveResult)
            return;

        foreach (var (_, value) in retrievedState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.ID;

            var deleteResult = await channelApi
                .DeleteChannelAsync(channelId);
            if (!deleteResult.IsSuccess)
                logger.LogError("Failed to delete player's channel: {Reason}, inner: {Inner}",
                    deleteResult.Error.Message, deleteResult.Inner);

            state.GameStates.ChinesePokerGameStates.Item2.TryRemove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        gameState.Dispose();
        state.GameStates.ChinesePokerGameStates.Item1.Remove(gameState.GameId, out _);
        socketSession.Dispose();
        throw new NotImplementedException();
    }

    public static Task<bool> HandleProgressChange(RawNinetyNineGameState deserializedIncomingData, NinetyNineGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }
}