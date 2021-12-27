using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Threading.Channels;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.ChinesePoker;

using ChinesePokerGame = IGame<ChinesePokerGameState, RawChinesePokerGameState, ChinesePoker, ChinesePokerGameProgress, ChinesePokerInGameRequestType>;

#pragma warning disable CA2252
public partial class ChinesePoker : IGame<ChinesePokerGameState, RawChinesePokerGameState, ChinesePoker, ChinesePokerGameProgress, ChinesePokerInGameRequestType>
{
    private static readonly ImmutableList<string> InGameRequestTypes =
        Enum.GetNames<ChinesePokerInGameRequestType>()
            .ToImmutableList();

    private static async Task StartListening(
        string gameId,
        Config config,
        GameStates gameStates,
        DeckService deckService,
        Localizations localizations,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var state = gameStates.ChinesePokerGameStates.Item1
            .GetOrAdd(gameId, new ChinesePokerGameState());
        logger.LogDebug("Chinese Poker game progress: {Progress}", state.Progress);

        await state.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        if (state.Progress != ChinesePokerGameProgress.NotAvailable)
        {
            state.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }
        
        state.Progress = ChinesePokerGameProgress.Starting;
        state.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");
        
        var buffer = ArrayPool<byte>.Create(Constants.BufferSize, 1024);

        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession = await Game.GenericOpenWebSocketSession(GameName, config, logger, cancellationTokenSource);
        state.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");

        var timeout = config.Timeout;
        while (!state.Progress.Equals(ChinesePokerGameProgress.Closed))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = ChinesePokerGame.RunChannelTask(socketSession, state, InGameRequestTypes,
                ChinesePokerInGameRequestType.Close, ChinesePokerGameProgress.Closed, logger, cancellationTokenSource);

            var broadcastTask = ChinesePokerGame.RunBroadcastTask(socketSession, state, gameStates, buffer, deckService,
                localizations, logger, cancellationTokenSource);

            var timeoutTask = ChinesePokerGame.RunTimeoutTask(timeout, state, ChinesePokerGameProgress.Closed, logger,
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
        var retrieveResult = gameStates.ChinesePokerGameStates.Item1.TryGetValue(state.GameId, out var gameState);
        if (!retrieveResult)
            return;

        foreach (var (_, value) in gameState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.Id;
            await value.TextChannel.DeleteAsync();
            gameStates.ChinesePokerGameStates.Item2.Remove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        gameStates.ChinesePokerGameStates.Item1.Remove(state.GameId, out _);
        socketSession.Dispose();
    }

    private static async Task AddChinesePokerPlayerState(string gameId, DiscordGuild guild,
        ChinesePokerPlayerState playerState, GameStates gameStates, float baseBet)
    {
        var state = gameStates
            .ChinesePokerGameStates
            .Item1
            .GetOrAdd(gameId, new ChinesePokerGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.Id);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChinesePokerInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                BaseBet = baseBet,
                ChannelId = playerState.TextChannel.Id,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName
            })));
    }

    public static async Task<bool> HandleProgress(byte[] messageContent, ChinesePokerGameState state, GameStates gameStates,
        DeckService deckService, Localizations localizations, ILogger logger)
    {
        try
        {
            var content = Encoding.UTF8.GetString(messageContent);
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawChinesePokerGameState>(content, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await state.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!state.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData, state, gameStates,
                    deckService, localizations);
                state.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await UpdateGameState(state, deserializedIncomingData);
            if (state.Progress.Equals(ChinesePokerGameProgress.Starting))
            {
                var result = state.Players.TryGetValue(deserializedIncomingData.PreviousPlayerId,
                    out var previousPlayerState);
                if (!result)
                {
                    state.Semaphore.Release();
                    return false;
                }

                await SendInitialMessage(previousPlayerState!, deckService, localizations, logger);
            }
            state.Semaphore.Release();
            logger.LogDebug("Semaphore released after sending progress messages");
        }
        catch (JsonSerializationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("An exception occurred when handling progress: {Exception}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
            state.Semaphore.Release();
            logger.LogDebug("Semaphore released in an exception");
            return false;
        }

        return true;
    }

    private static async Task<bool> HandleProgressChange(
        RawChinesePokerGameState deserializedIncomingData,
        ChinesePokerGameState state,
        GameStates gameStates,
        DeckService deckService,
        Localizations localizations)
    {
        if (deserializedIncomingData.Progress.Equals(ChinesePokerGameProgress.Ending))
            return true;

        state.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(state, deserializedIncomingData);

        switch (deserializedIncomingData.Progress)
        {
            case ChinesePokerGameProgress.NotAvailable:
            case ChinesePokerGameProgress.Starting:
            case ChinesePokerGameProgress.Ending:
            case ChinesePokerGameProgress.Closed:
                break;
            case ChinesePokerGameProgress.FrontHand:
            case ChinesePokerGameProgress.MiddleHand:
            case ChinesePokerGameProgress.BackHand:
            {
                foreach (var (_, playerState) in state.Players)
                {
                    if (playerState.TextChannel == null)
                        continue;

                    gameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.Id);
                    await playerState.TextChannel.SendMessageAsync(BuildHandMessage(playerState,
                        deserializedIncomingData.Progress, deckService, localizations));
                }
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported Chinese poker game progress.");
        }

        return true;
    }

    public static Task HandleEndingResult(byte[] messageContent, ChinesePokerGameState state, Localizations localizations,
        ILogger logger)
    {
        throw new NotImplementedException();
    }

    private static async Task SendInitialMessage(ChinesePokerPlayerState playerState, DeckService deckService, Localizations localizations, ILogger logger)
    {
        if (playerState.TextChannel == null)
            return;
        
        logger.LogDebug("Sending initial message...");
        var localization = localizations.GetLocalization().ChinesePoker;
        var prefix = localization.InitialMessagePrefix;
        var cardNames = prefix + string.Join('\n', playerState.Cards.Select(c => c.ToString()));
        var description = localization.InitialMessageDescription
            .Replace("{cardNames}", cardNames);

        var deck = SkiaService.RenderChinesePokerDeck(deckService, playerState.Cards);
        await playerState.TextChannel.SendMessageAsync(new DiscordMessageBuilder()
            .AddEmbed(new DiscordEmbedBuilder()
                .WithAuthor(playerState.PlayerName, iconUrl: playerState.AvatarUrl)
                .WithColor((int)BurstColor.Burst)
                .WithTitle(localization.InitialMessageTitle)
                .WithDescription(description)
                .WithFooter(localization.InitialMessageFooter)
                .WithThumbnail(Constants.BurstLogo)
                .WithImageUrl(Constants.AttachmentUri))
            .WithFile(Constants.OutputFileName, deck));
    }

    private static DiscordMessageBuilder BuildHandMessage(
        ChinesePokerPlayerState playerState,
        ChinesePokerGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations)
    {
        var localization = localizations.GetLocalization();

        var title = nextProgress switch
        {
            ChinesePokerGameProgress.FrontHand => "Front Hand",
            ChinesePokerGameProgress.MiddleHand => "Middle Hand",
            ChinesePokerGameProgress.BackHand => "Back Hand",
            _ => ""
        };

        var description = nextProgress switch
        {
            ChinesePokerGameProgress.FrontHand => localization.ChinesePoker.CommandList["fronthand"],
            ChinesePokerGameProgress.MiddleHand => localization.ChinesePoker.CommandList["middlehand"]
                .Replace("{hand}", title.ToLowerInvariant()),
            ChinesePokerGameProgress.BackHand => localization.ChinesePoker.CommandList["backhand"]
                .Replace("{hand}", title.ToLowerInvariant()),
            _ => ""
        };

        var embed = new DiscordEmbedBuilder()
            .WithColor(new DiscordColor((int)BurstColor.Burst))
            .WithDescription($"{description}\n\nYour cards:")
            .WithFooter("Type and separate ranks with space to set your hand. E.g. A 3 4")
            .WithTitle(title)
            .WithThumbnail(Constants.BurstLogo)
            .WithImageUrl(Constants.AttachmentUri);

        var deck = SkiaService.RenderChinesePokerDeck(deckService, playerState.Cards);

        return new DiscordMessageBuilder().WithEmbed(embed).WithFile(Constants.OutputFileName, deck);
    }

    private static async Task UpdateGameState(ChinesePokerGameState state, RawChinesePokerGameState? data)
    {
        if (data == null)
            return;

        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.Units = data.Units;
        state.BaseBet = data.BaseBet;

        foreach (var (playerId, playerState) in data.Players)
        {
            if (state.Players.ContainsKey(playerId))
            {
                var player = state.Players.GetOrAdd(playerId, new ChinesePokerPlayerState());
                player.Cards = playerState.Cards;
                player.Naturals = playerState.Naturals;
                player.AvatarUrl = playerState.AvatarUrl;
                player.PlayedCards = playerState.PlayedCards;
                player.PlayerName = playerState.PlayerName;

                if (playerState.ChannelId == 0 || player.TextChannel != null) continue;
                foreach (var guild in state.Guilds)
                {
                    var channels = await guild.GetChannelsAsync();
                    if (channels == null || !channels.Any()) continue;
                    var textChannel = channels
                        .FirstOrDefault(c => c.Id == playerState.ChannelId);
                    if (textChannel == null) continue;
                    player.TextChannel = textChannel;
                }
            }
            else
            {
                var newPlayerState = new ChinesePokerPlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards,
                    GameId = playerState.GameId,
                    Naturals = playerState.Naturals,
                    PlayedCards = playerState.PlayedCards,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null,
                };
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}