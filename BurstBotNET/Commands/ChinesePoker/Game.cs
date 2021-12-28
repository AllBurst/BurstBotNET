using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.ChinesePoker;

using ChinesePokerGame = IGame<ChinesePokerGameState, RawChinesePokerGameState, ChinesePoker, ChinesePokerGameProgress, ChinesePokerInGameRequestType>;

#pragma warning disable CA2252
public partial class ChinesePoker : ChinesePokerGame
{
    private static readonly ImmutableArray<string> InGameRequestTypes =
        Enum.GetNames<ChinesePokerInGameRequestType>()
            .ToImmutableArray();

    private static readonly Regex CardRegex = new(@"([shdc])([0-9ajqk]+)");
    private static readonly string[] AvailableRanks;

    private static readonly ChinesePokerGameProgress[] Hands = {
        ChinesePokerGameProgress.FrontHand, ChinesePokerGameProgress.MiddleHand,
        ChinesePokerGameProgress.BackHand
    };

    private static async Task StartListening(
        string gameId,
        State state,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        var gameState = state.GameStates.ChinesePokerGameStates.Item1
            .GetOrAdd(gameId, new ChinesePokerGameState());
        logger.LogDebug("Chinese Poker game progress: {Progress}", gameState.Progress);

        await gameState.Semaphore.WaitAsync();
        logger.LogDebug("Semaphore acquired in StartListening");
        if (gameState.Progress != ChinesePokerGameProgress.NotAvailable)
        {
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in StartListening (game state existed)");
            return;
        }
        
        gameState.Progress = ChinesePokerGameProgress.Starting;
        gameState.GameId = gameId;
        logger.LogDebug("Initial game state successfully set");
        
        var buffer = ArrayPool<byte>.Create(Constants.BufferSize, 1024);

        var cancellationTokenSource = new CancellationTokenSource();
        var socketSession = await Game.GenericOpenWebSocketSession(GameName, state.Config, logger, cancellationTokenSource);
        gameState.Semaphore.Release();
        logger.LogDebug("Semaphore released in StartListening (game state created)");

        var timeout = state.Config.Timeout;
        while (!gameState.Progress.Equals(ChinesePokerGameProgress.Closed))
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var channelTask = ChinesePokerGame.RunChannelTask(socketSession, gameState, InGameRequestTypes,
                ChinesePokerInGameRequestType.Close, ChinesePokerGameProgress.Closed, logger, cancellationTokenSource);

            var broadcastTask = ChinesePokerGame.RunBroadcastTask(socketSession, gameState, buffer, state,
                logger, cancellationTokenSource);

            var timeoutTask = ChinesePokerGame.RunTimeoutTask(timeout, gameState, ChinesePokerGameProgress.Closed, logger,
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
        var retrieveResult = state.GameStates.ChinesePokerGameStates.Item1.TryGetValue(gameState.GameId, out var retrievedState);
        if (!retrieveResult)
            return;

        foreach (var (_, value) in retrievedState!.Players)
        {
            if (value.TextChannel == null)
                continue;

            var channelId = value.TextChannel.Id;
            await value.TextChannel.DeleteAsync();
            state.GameStates.ChinesePokerGameStates.Item2.Remove(channelId);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        state.GameStates.ChinesePokerGameStates.Item1.Remove(gameState.GameId, out _);
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

    private static async Task<bool> HandleSettingHand(
        ChinesePokerGameState state,
        ChinesePokerPlayerState playerState,
        ImmutableArray<string> splitMessage,
        DiscordInteraction interaction,
        long timeout,
        DeckService deckService,
        Localizations localizations
    )
    {
        var localization = localizations.GetLocalization().ChinesePoker;

        var handName = state.Progress switch
        {
            ChinesePokerGameProgress.FrontHand => localization.FrontHand.ToLowerInvariant(),
            ChinesePokerGameProgress.MiddleHand => localization.MiddleHand.ToLowerInvariant(),
            ChinesePokerGameProgress.BackHand => localization.BackHand.ToLowerInvariant(),
            _ => ""
        };
        
        if (splitMessage.Any(s => !CardRegex.IsMatch(s)))
        {
            await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent(localization.InvalidCard));
            return false;
        }
        
        var cardMatches = splitMessage
            .Select(s => CardRegex.Match(s))
            .ToImmutableArray();
        
        if (cardMatches.Any(m => !AvailableRanks.Contains(m.Groups[2].Value)))
        {
            await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent(localization.InvalidCard));
            return false;
        }

        var cards = cardMatches
            .Select(m => Card.CreateCard(m.Groups[1].Value, m.Groups[2].Value))
            .ToImmutableArray();

        if (cards.Any(c => !playerState.Cards.Contains(c)))
        {
            await interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent(localization.InvalidCard));
            return false;
        }
        
        var renderedCards = SkiaService.RenderDeck(deckService, cards);
        var reply = new DiscordFollowupMessageBuilder()
            .AddEmbed(new DiscordEmbedBuilder()
                .WithAuthor(playerState.PlayerName, iconUrl: playerState.AvatarUrl)
                .WithColor((int)BurstColor.Burst)
                .WithDescription($"{localization.Cards}\n{string.Join('\n', cards)}\n{localization.ConfirmCards.Replace("{hand}", handName)}")
                .WithFooter(localization.ConfirmCardsFooter.Replace("{hand}", handName))
                .WithThumbnail(Constants.BurstLogo)
                .WithTitle(TextInfo.ToTitleCase(handName))
                .WithImageUrl(Constants.AttachmentUri))
            .AddComponents(
                new DiscordButtonComponent(ButtonStyle.Primary, "confirm", localization.Confirm, emoji: new DiscordComponentEmoji(Constants.CheckMarkEmoji)),
                new DiscordButtonComponent(ButtonStyle.Danger, "cancel", localization.Cancel, emoji: new DiscordComponentEmoji(Constants.CrossMarkEmoji))
                )
            .AddFile(Constants.OutputFileName, renderedCards, true);

        var confirmMessage = await interaction.CreateFollowupMessageAsync(reply);
        var confirmationAction = await confirmMessage
            .WaitForButtonAsync(playerState.Member, TimeSpan.FromSeconds(timeout));
        var customId = confirmationAction.Result.Interaction.Data.CustomId;

        if (confirmationAction.TimedOut)
        {
            await interaction.EditFollowupMessageAsync(confirmMessage.Id, new DiscordWebhookBuilder()
                .WithContent(localization.ConfirmCardsFailure));
            return false;
        }

        if (string.Equals(customId, "cancel"))
        {
            await interaction.EditFollowupMessageAsync(confirmMessage.Id, new DiscordWebhookBuilder()
                .WithContent(Constants.CrossMarkEmoji.GetDiscordName()));
            return false;
        }
        
        var currentProgress = state.Progress;
        playerState.DeckImages.Add(currentProgress, renderedCards);
        await state.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChinesePokerInGameRequest
            {
                RequestType = state.Progress switch
                {
                    ChinesePokerGameProgress.FrontHand => ChinesePokerInGameRequestType.FrontHand,
                    ChinesePokerGameProgress.MiddleHand => ChinesePokerInGameRequestType.MiddleHand,
                    ChinesePokerGameProgress.BackHand => ChinesePokerInGameRequestType.BackHand,
                    _ => ChinesePokerInGameRequestType.Close
                },
                GameId = state.GameId,
                PlayerId = playerState.PlayerId,
                PlayCard = cards
            })));

        await interaction.EditFollowupMessageAsync(confirmMessage.Id, new DiscordWebhookBuilder()
            .WithContent(Constants.CheckMarkEmoji.GetDiscordName()));
        return true;
    }

    public static async Task<bool> HandleProgress(
        string messageContent,
        ChinesePokerGameState gameState,
        State state,
        ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawChinesePokerGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData, gameState, state, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await UpdateGameState(gameState, deserializedIncomingData, logger);
            if (gameState.Progress.Equals(ChinesePokerGameProgress.Starting))
            {
                var result = gameState.Players.TryGetValue(deserializedIncomingData.PreviousPlayerId,
                    out var previousPlayerState);
                if (!result)
                {
                    gameState.Semaphore.Release();
                    return false;
                }

                await SendInitialMessage(previousPlayerState!, state.DeckService, state.Localizations, logger);
            }
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released after sending progress messages");
        }
        catch (JsonSerializationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("An exception occurred when handling progress: {Exception}", ex);
            logger.LogError("Exception message: {Message}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
            gameState.Semaphore.Release();
            logger.LogDebug("Semaphore released in an exception");
            return false;
        }

        return true;
    }

    private static async Task<bool> HandleProgressChange(
        RawChinesePokerGameState deserializedIncomingData,
        ChinesePokerGameState gameState,
        State state,
        ILogger logger)
    {
        if (deserializedIncomingData.Progress.Equals(ChinesePokerGameProgress.Ending))
            return true;

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, logger);

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
                foreach (var (_, playerState) in gameState.Players)
                {
                    if (playerState.TextChannel == null)
                        continue;

                    state.GameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.Id);
                    var message = await playerState.TextChannel.SendMessageAsync(BuildHandMessage(playerState,
                        deserializedIncomingData.Progress, state.DeckService, state.Localizations));
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var result = await message
                                .WaitForSelectAsync(playerState.Member, "cards", null);

                            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType
                                .DeferredChannelMessageWithSource);

                            result.Result.Handled = await HandleSettingHand(gameState, playerState,
                                result.Result.Values.ToImmutableArray(), result.Result.Interaction,
                                state.Config.Timeout, state.DeckService, state.Localizations);
                            if (result.Result.Handled)
                                break;
                        }
                    });
                }
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported Chinese poker game progress.");
        }

        return true;
    }

    public static async Task HandleEndingResult(string messageContent,
        ChinesePokerGameState state,
        Localizations localizations,
        DeckService deckService,
        ILogger logger)
    {
        logger.LogDebug("Handling ending result...");

        try
        {
            var deserializedEndingData =
                JsonSerializer.Deserialize<ChinesePokerInGameResponseEndingData>(messageContent);
            if (deserializedEndingData == null)
                return;

            state.Progress = deserializedEndingData.Progress;
            var (key, _) = deserializedEndingData
                .TotalRewards
                .MaxBy(rewardData => rewardData.Value.Units);
            var winner = state
                .Players
                .Where(p => p.Value.PlayerId == key)
                .Select(p => p.Value)
                .First();

            var localization = localizations.GetLocalization().ChinesePoker;
            var title = localization.WinTitle.Replace("{playerName}", winner.PlayerName);
            var playerResults = deserializedEndingData
                .TotalRewards
                .Select(reward =>
                {
                    var (pId, pReward) = reward;
                    var playerName = state
                        .Players
                        .Where(p => p.Key == pId)
                        .Select(p => p.Value.PlayerName)
                        .FirstOrDefault();
                    return localization.WinDescription
                        .Replace("{playerName}", playerName)
                        .Replace("{verb}", pReward.Units > 0 ? localization.Won : localization.Lost)
                        .Replace("{totalRewards}", Math.Abs(pReward.Units).ToString());
                })
                .ToImmutableArray();

            var descriptionBuilder = new StringBuilder();
            descriptionBuilder.Append(string.Join('\n', playerResults) + "\n\n");
            var hands = new[]
            {
                localization.FrontHand,
                localization.MiddleHand,
                localization.BackHand
            };
            var zippedHands = Hands.Zip(hands).ToImmutableArray();

            foreach (var (handType, hand) in zippedHands)
            {
                await ShowAllHands(handType, hand, deserializedEndingData, state, deckService);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            foreach (var (handType, hand) in zippedHands)
            {
                descriptionBuilder.Append($"**{hand}**\n");
                var hs = deserializedEndingData
                    .Players
                    .Values
                    .Select(v => (v.PlayerName, v.PlayedCards[handType]))
                    .ToImmutableArray();
                foreach (var (playerName, combination) in hs)
                {
                    var desc = $"{playerName} - *{combination.CombinationType}*\n{string.Join('\n', combination.Cards)}";
                    descriptionBuilder.Append(desc + '\n');
                }

                descriptionBuilder.Append("\n\n");
            }

            var embed = new DiscordEmbedBuilder()
                .WithColor((int)BurstColor.Burst)
                .WithDescription(descriptionBuilder.ToString())
                .WithThumbnail(Constants.BurstLogo)
                .WithTitle(title);

            foreach (var (pId, pReward) in deserializedEndingData.TotalRewards)
            {
                var builder = new StringBuilder();
                builder.Append($"{pReward.Units} tips\n");
                
                if (pReward.Rewards.Any())
                {
                    builder.Append(string.Join('\n', pReward.Rewards.Select(r => r.RewardType.ToString())));
                }
                
                var playerName = state
                    .Players
                    .Where(p => p.Value.PlayerId == pId)
                    .Select(p => p.Value.PlayerName)
                    .FirstOrDefault();
                embed = embed.AddField(playerName, builder.ToString(), true);
            }

            foreach (var (_, playerState) in state.Players)
            {
                if (playerState.TextChannel == null)
                    continue;

                await playerState.TextChannel.SendMessageAsync(embed);
            }

            await state.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                0, JsonSerializer.SerializeToUtf8Bytes(new ChinesePokerInGameRequest
                {
                    GameId = state.GameId,
                    RequestType = ChinesePokerInGameRequestType.Close,
                    PlayerId = 0
                })));
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(messageContent))
                return;
            
            logger.LogError("An exception occurred when handling ending result: {Exception}", ex);
            logger.LogError("Exception message: {Message}", ex.Message);
            logger.LogError("Source: {Source}", ex.Source);
            logger.LogError("Stack trace: {Trace}", ex.StackTrace);
            logger.LogError("Message content: {Content}", messageContent);
        }
    }

    private static async Task ShowAllHands(
        ChinesePokerGameProgress progress,
        string handName,
        ChinesePokerInGameResponseEndingData endingData,
        ChinesePokerGameState gameState,
        DeckService deckService)
    {
        var playerStates = gameState
            .Players
            .Select(pair => pair.Value)
            .ToImmutableArray();
        var renderedImage = await SkiaService.RenderChinesePokerHand(playerStates, progress, deckService);

        foreach (var (_, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null)
                continue;
            
            var randomFileName = Utilities.GenerateRandomString() + ".jpg";
            var randomAttachmentUri = $"attachment://{randomFileName}";
            
            var embed = new DiscordEmbedBuilder()
                .WithColor((int)BurstColor.Burst)
                .WithTitle(handName)
                .WithThumbnail(Constants.BurstLogo)
                .WithImageUrl(randomAttachmentUri);

            foreach (var (_, pState) in endingData.Players)
            {
                var fieldName = pState.PlayerName;
                var fieldValue =
                    $"**{pState.PlayedCards[progress].CombinationType}**\n{string.Join('\n', pState.PlayedCards[progress].Cards)}";
                embed = embed.AddField(fieldName, fieldValue, true);
            }

            await playerState.TextChannel.SendMessageAsync(new DiscordMessageBuilder()
                .WithEmbed(embed)
                .WithFile(randomFileName, renderedImage, true));

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        
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
            .WithFile(Constants.OutputFileName, deck, true));
        playerState.DeckImages.Add(ChinesePokerGameProgress.Starting, deck);
    }

    private static DiscordMessageBuilder BuildHandMessage(
        ChinesePokerPlayerState playerState,
        ChinesePokerGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations)
    {
        var localization = localizations.GetLocalization();
        var chinesePokerLocalization = localization.ChinesePoker;

        var title = nextProgress switch
        {
            ChinesePokerGameProgress.FrontHand => chinesePokerLocalization.FrontHand,
            ChinesePokerGameProgress.MiddleHand => chinesePokerLocalization.MiddleHand,
            ChinesePokerGameProgress.BackHand => chinesePokerLocalization.BackHand,
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
            .WithDescription($"{description}\n\n{chinesePokerLocalization.Cards}")
            .WithFooter(chinesePokerLocalization.SetHandFooter)
            .WithTitle(title)
            .WithThumbnail(Constants.BurstLogo)
            .WithImageUrl(Constants.AttachmentUri);

        Stream? deck = null;
        switch (nextProgress)
        {
            case ChinesePokerGameProgress.FrontHand:
                deck = playerState.DeckImages[ChinesePokerGameProgress.Starting];
                break;
            case ChinesePokerGameProgress.MiddleHand:
            case ChinesePokerGameProgress.BackHand:
                deck = SkiaService.RenderChinesePokerDeck(deckService, playerState.Cards);
                break;
        }

        var requiredCardCount = nextProgress switch
        {
            ChinesePokerGameProgress.FrontHand => 3,
            ChinesePokerGameProgress.MiddleHand or ChinesePokerGameProgress.BackHand => 5,
            _ => 0
        };

        var placeholder = chinesePokerLocalization.DropDownMessage
            .Replace("{num}", requiredCardCount.ToString())
            .Replace("{hand}", nextProgress switch
            {
                ChinesePokerGameProgress.FrontHand => chinesePokerLocalization.FrontHand.ToLowerInvariant(),
                ChinesePokerGameProgress.MiddleHand => chinesePokerLocalization.MiddleHand.ToLowerInvariant(),
                ChinesePokerGameProgress.BackHand => chinesePokerLocalization.BackHand.ToLowerInvariant(),
                _ => ""
            });
        
        var activeCards = playerState.Cards
            .Where(c => c.IsFront)
            .Select(c => new DiscordSelectComponentOption(c.ToStringSimple(),
                c.ToSpecifier(),
                c.ToStringSimple(), emoji: c.Suit.ToComponentEmoji()))
            .ToImmutableArray();

        return new DiscordMessageBuilder()
            .WithEmbed(embed)
            .WithFile(Constants.OutputFileName, deck, true)
            .AddComponents(new DiscordSelectComponent("cards", placeholder, activeCards, minOptions: requiredCardCount,
                maxOptions: requiredCardCount));
    }

    private static async Task UpdateGameState(ChinesePokerGameState state, RawChinesePokerGameState? data, ILogger logger)
    {
        if (data == null)
            return;

        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.Units = data.Units;
        state.BaseBet = data.BaseBet;

        foreach (var (playerId, playerState) in data.Players)
        {
            playerState.Cards.Sort((a, b) => a.Number.CompareTo(b.Number));
            if (state.Players.ContainsKey(playerId))
            {
                var player = state.Players.GetOrAdd(playerId, new ChinesePokerPlayerState());
                player.Cards = playerState.Cards;
                player.Naturals = playerState.Naturals;
                player.AvatarUrl = playerState.AvatarUrl;
                player.PlayedCards = playerState.PlayedCards;
                player.PlayerName = playerState.PlayerName;

                if (player.Member == null)
                {
                    foreach (var guild in state.Guilds)
                    {
                        try
                        {
                            var member = await guild.GetMemberAsync(player.PlayerId);
                            if (member == null) continue;
                            player.Member = member;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Exception caught when getting member: {Exception}", ex);
                        }
                    }
                }
                
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
                
                foreach (var guild in state.Guilds)
                {
                    try
                    {
                        var member = await guild.GetMemberAsync(playerState.PlayerId);
                        if (member == null) continue;
                        newPlayerState.Member = member;
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Exception caught when getting member: {Exception}", ex);
                    }
                }
                
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}