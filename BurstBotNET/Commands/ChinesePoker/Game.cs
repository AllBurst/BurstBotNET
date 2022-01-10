using System.Collections.Immutable;
using System.Globalization;
using System.Text;
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
using BurstBotShared.Shared.Models.Localization.ChinesePoker.Serializables;
using Microsoft.Extensions.Logging;
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

namespace BurstBotNET.Commands.ChinesePoker;

using ChinesePokerGame =
    IGame<ChinesePokerGameState, RawChinesePokerGameState, ChinesePoker, ChinesePokerPlayerState,
        ChinesePokerGameProgress,
        ChinesePokerInGameRequestType>;

#pragma warning disable CA2252
public partial class ChinesePoker : ChinesePokerGame
{
    private static readonly ChinesePokerGameProgress[] Hands =
    {
        ChinesePokerGameProgress.FrontHand, ChinesePokerGameProgress.MiddleHand,
        ChinesePokerGameProgress.BackHand
    };

    public static async Task<bool> HandleProgress(
        string messageContent,
        ChinesePokerGameState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
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
                var progressChangeResult =
                    await HandleProgressChange(deserializedIncomingData, gameState, state, channelApi, guildApi,
                        logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
            if (gameState.Progress.Equals(ChinesePokerGameProgress.Starting))
            {
                var result = gameState.Players.TryGetValue(deserializedIncomingData.PreviousPlayerId,
                    out var previousPlayerState);
                if (!result)
                {
                    gameState.Semaphore.Release();
                    return false;
                }

                await SendInitialMessage(
                    gameState,
                    previousPlayerState!,
                    state.DeckService, state.Localizations,
                    channelApi, logger);
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
            return Utilities.HandleException(ex, messageContent, gameState.Semaphore, logger);
        }

        return true;
    }

    public static async Task HandleEndingResult(string messageContent,
        ChinesePokerGameState state,
        Localizations localizations,
        DeckService deckService,
        IDiscordRestChannelAPI channelApi,
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

            if (!deserializedEndingData.DeclaredNatural.HasValue)
            {
                var hands = new[]
                {
                    localization.FrontHand,
                    localization.MiddleHand,
                    localization.BackHand
                };
                var zippedHands = Hands.Zip(hands).ToImmutableArray();

                foreach (var (handType, hand) in zippedHands)
                {
                    await ShowAllHands(handType, hand, deserializedEndingData, state, deckService, localization,
                        channelApi, logger);
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
                        var desc =
                            $"{playerName} - *{combination.CombinationType.ToLocalizedString(localization)}*\n{string.Join('\n', combination.Cards.ToImmutableArray().Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue())))}";
                        descriptionBuilder.Append(desc + '\n');
                    }

                    descriptionBuilder.Append("\n\n");
                }
            }
            else
            {
                var naturalMessage = localization.NaturalHit
                    .Replace("{player}", winner.PlayerName)
                    .Replace("{natural}", deserializedEndingData
                        .DeclaredNatural.Value.ToLocalizedString(localization));
                descriptionBuilder.Append(naturalMessage);

                await ShowNatural(winner, naturalMessage, deserializedEndingData.Players[winner.PlayerId].PlayedCards,
                    state, deckService, channelApi, logger);
            }

            var fields = new List<EmbedField>(state.Players.Count);

            foreach (var (pId, pReward) in deserializedEndingData.TotalRewards)
            {
                var builder = new StringBuilder();
                builder.Append($"{pReward.Units} tips\n");

                if (pReward.Rewards.Any())
                    builder.Append(string.Join('\n',
                        pReward.Rewards.Select(r => r.RewardType.ToLocalizedString(localization))));

                var playerName = state
                    .Players
                    .Where(p => p.Value.PlayerId == pId)
                    .Select(p => p.Value.PlayerName)
                    .FirstOrDefault();

                fields.Add(new EmbedField(playerName!, builder.ToString(), true));
            }

            var embed = new Embed(
                Colour: BurstColor.Burst.ToColor(),
                Description: descriptionBuilder.ToString(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
                Title: title,
                Fields: fields);

            foreach (var (_, playerState) in state.Players)
            {
                if (playerState.TextChannel == null)
                    continue;

                var sendEmbedResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID, embeds: new[] { embed });

                if (!sendEmbedResult.IsSuccess)
                    logger.LogError("Failed to send ending result to player's channel: {Reason}, inner: {Inner}",
                        sendEmbedResult.Error.Message, sendEmbedResult.Inner);
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

    public static async Task AddPlayerState(string gameId, Snowflake guild,
        ChinesePokerPlayerState playerState, GameStates gameStates)
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

        gameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChinesePokerInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel.ID.Value,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName
            })));
    }

    public static async Task<bool> HandleProgressChange(
        RawChinesePokerGameState deserializedIncomingData,
        ChinesePokerGameState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        if (deserializedIncomingData.Progress.Equals(ChinesePokerGameProgress.Ending))
            return true;

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);

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
                    await SendHandMessage(playerState, state, deserializedIncomingData,
                        channelApi, logger);

                break;
            }
            default:
                throw new InvalidOperationException("Unsupported Chinese poker game progress.");
        }

        return true;
    }

    private static async Task ShowAllHands(
        ChinesePokerGameProgress progress,
        string handName,
        ChinesePokerInGameResponseEndingData endingData,
        ChinesePokerGameState gameState,
        DeckService deckService,
        ChinesePokerLocalization localization,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
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

            var embed = new EmbedBuilder()
                .WithColour(BurstColor.Burst.ToColor())
                .WithTitle(handName)
                .WithThumbnailUrl(Constants.BurstLogo);

            foreach (var (_, pState) in endingData.Players)
            {
                var fieldName = pState.PlayerName;
                var fieldValue =
                    $"**{pState.PlayedCards[progress].CombinationType.ToLocalizedString(localization)}**\n{string.Join('\n', pState.PlayedCards[progress].Cards.ToImmutableArray().Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue())))}";
                embed.AddField(fieldName, fieldValue, true);
            }

            await using var imageCopy = new MemoryStream((int)renderedImage.Length);
            await renderedImage.CopyToAsync(imageCopy);
            renderedImage.Seek(0, SeekOrigin.Begin);
            imageCopy.Seek(0, SeekOrigin.Begin);

            var sendEmbedResult = await channelApi
                .CreateMessageAsync(playerState.TextChannel.ID,
                    embeds: new[] { embed.Build().Entity with { Image = new EmbedImage(randomAttachmentUri) } },
                    attachments: new[]
                        { OneOf<FileData, IPartialAttachment>.FromT0(new FileData(randomFileName, imageCopy)) });
            if (!sendEmbedResult.IsSuccess)
                logger.LogError("Failed to send embed to player's channel: {Reason}, inner: {Inner}",
                    sendEmbedResult.Error.Message, sendEmbedResult.Inner);

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task ShowNatural(
        ChinesePokerPlayerState winner,
        string title,
        Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> winnerPlayedCards,
        ChinesePokerGameState gameState,
        DeckService deckService,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var renderedImage =
            await SkiaService.RenderChinesePokerNatural(winner, winnerPlayedCards, Hands.ToImmutableArray(),
                deckService);
        foreach (var (_, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null)
                continue;

            var randomFileName = Utilities.GenerateRandomString() + ".jpg";
            var randomAttachmentUri = $"attachment://{randomFileName}";

            var embed = new EmbedBuilder()
                    .WithColour(BurstColor.Burst.ToColor())
                    .WithTitle(title.ToUpperInvariant())
                    .WithThumbnailUrl(Constants.BurstLogo)
                    .Build()
                    .Entity with
                {
                    Image = new EmbedImage(randomAttachmentUri)
                };

            await using var imageCopy = new MemoryStream((int)renderedImage.Length);
            await renderedImage.CopyToAsync(imageCopy);
            renderedImage.Seek(0, SeekOrigin.Begin);
            imageCopy.Seek(0, SeekOrigin.Begin);

            var sendEmbedResult = await channelApi
                .CreateMessageAsync(playerState.TextChannel.ID,
                    embeds: new[] { embed },
                    attachments: new[]
                        { OneOf<FileData, IPartialAttachment>.FromT0(new FileData(randomFileName, imageCopy)) });
            renderedImage.Seek(0, SeekOrigin.Begin);
            if (!sendEmbedResult.IsSuccess)
                logger.LogError("Failed to send embed to player's channel: {Reason}, inner: {Inner}",
                    sendEmbedResult.Error.Message, sendEmbedResult.Inner);

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task SendInitialMessage(
        ChinesePokerGameState gameState,
        ChinesePokerPlayerState playerState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (playerState.TextChannel == null)
            return;

        logger.LogDebug("Sending initial message...");
        var localization = localizations.GetLocalization().ChinesePoker;
        var prefix = localization.InitialMessagePrefix;
        var cardNames = prefix + string.Join('\n', playerState.Cards.Select(c => c.ToString()));
        var description = localization.InitialMessageDescription
            .Replace("{cardNames}", cardNames)
            .Replace("{baseBet}", gameState.BaseBet.ToString(CultureInfo.InvariantCulture));

        var deck = SkiaService.RenderChinesePokerDeck(deckService,
            playerState.Cards);

        var embed = new Embed(
            Author: new EmbedAuthor(playerState.PlayerName, IconUrl: playerState.AvatarUrl),
            Colour: BurstColor.Burst.ToColor(),
            Title: localization.InitialMessageTitle,
            Description: description,
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Image: new EmbedImage(Constants.AttachmentUri));

        await using var deckCopy = new MemoryStream((int)deck.Length);
        await deck.CopyToAsync(deckCopy);
        deck.Seek(0, SeekOrigin.Begin);
        deckCopy.Seek(0, SeekOrigin.Begin);

        var sendEmbedResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { embed },
                attachments: new[]
                    { OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, deckCopy)) });
        if (!sendEmbedResult.IsSuccess)
        {
            var restError = (RestResultError<RestError>)sendEmbedResult.Error;
            logger.LogError("Failed to send initial message to player's channel: {Reason}, inner: {Inner}",
                restError.Message, restError.Error);
        }

        playerState.DeckImages.Add(ChinesePokerGameProgress.Starting, deck);
    }

    private static async Task SendHandMessage(
        ChinesePokerPlayerState playerState,
        State state,
        RawChinesePokerGameState deserializedIncomingData,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (playerState.TextChannel == null)
            return;

        state.GameStates.ChinesePokerGameStates.Item2.Add(playerState.TextChannel.ID);
        await BuildHandMessage(playerState,
            deserializedIncomingData.Progress, state.DeckService, channelApi, state.Localizations, logger);

        if (deserializedIncomingData.Progress.Equals(ChinesePokerGameProgress.BackHand))
            await BuildNaturalMessage(
                playerState,
                state.Localizations,
                channelApi,
                logger);
    }

    private static async Task BuildHandMessage(
        ChinesePokerPlayerState playerState,
        ChinesePokerGameProgress nextProgress,
        DeckService deckService,
        IDiscordRestChannelAPI channelApi,
        Localizations localizations,
        ILogger logger)
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

        var embed = new EmbedBuilder()
                .WithColour(BurstColor.Burst.ToColor())
                .WithDescription($"{description}\n\n{chinesePokerLocalization.Cards}")
                .WithTitle(title)
                .WithThumbnailUrl(Constants.BurstLogo)
                .Build()
                .Entity with
            {
                Image = new EmbedImage(Constants.AttachmentUri)
            };

        Stream? deck = null;
        switch (nextProgress)
        {
            case ChinesePokerGameProgress.FrontHand:
                deck = playerState.DeckImages[ChinesePokerGameProgress.Starting];
                break;
            case ChinesePokerGameProgress.MiddleHand:
            case ChinesePokerGameProgress.BackHand:
                deck = SkiaService.RenderChinesePokerDeck(deckService,
                    playerState.Cards);
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
            .Select(c => new SelectOption(c.ToStringSimple(),
                c.ToSpecifier(),
                c.ToStringSimple(), new PartialEmoji(c.Suit.ToSnowflake()), false))
            .ToImmutableArray();

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("chinese_poker_cards", activeCards, placeholder, requiredCardCount,
                    requiredCardCount, false)
            }),
            new ActionRowComponent(new[]
            {
                new ButtonComponent(ButtonComponentStyle.Primary, chinesePokerLocalization.ShowHelp,
                    new PartialEmoji(Name: "❓"), "chinese_poker_help")
            })
        };

        await using var deckCopy = new MemoryStream((int)deck!.Length);
        await deck.CopyToAsync(deckCopy);

        deck.Seek(0, SeekOrigin.Begin);
        deckCopy.Seek(0, SeekOrigin.Begin);

        var messageResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel!.ID,
                embeds: new[] { embed },
                attachments: new[]
                    { OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, deckCopy)) },
                components: components);

        if (!messageResult.IsSuccess)
            logger.LogError("Failed to send hand message to player: {Reason}, inner: {Inner}",
                messageResult.Error.Message, messageResult.Inner);
    }

    private static async Task BuildNaturalMessage(
        ChinesePokerPlayerState playerState,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        var chinesePokerLocalization = localizations.GetLocalization().ChinesePoker;
        var allNaturals = Enum.GetValues<ChinesePokerNatural>()
            .Select(n => (n, n.ToLocalizedString(chinesePokerLocalization)))
            .Select(pair => new SelectOption(pair.Item2, pair.n.ToString(), pair.Item2, default, false))
            .ToImmutableArray();

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("naturals", allNaturals, chinesePokerLocalization.DeclareNatural, 0, 1, false)
            })
        };

        var messageResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel!.ID,
                chinesePokerLocalization.DeclareNatural,
                components: components);

        if (!messageResult.IsSuccess)
            logger.LogError("Failed to send embed for natural: {Reason}, inner: {Inner}",
                messageResult.Error.Message, messageResult.Inner);
    }

    private static async Task UpdateGameState(
        ChinesePokerGameState state,
        RawChinesePokerGameState? data,
        IDiscordRestGuildAPI guildApi)
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
                var oldPlayerState = state.Players.GetOrAdd(playerId, new ChinesePokerPlayerState());
                oldPlayerState.Cards = playerState.Cards.ToImmutableArray()
                    .Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()));
                oldPlayerState.Naturals = playerState.Naturals;
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayedCards = playerState.PlayedCards
                    .ToDictionary(pair => pair.Key, pair =>
                    {
                        var (_, value) = pair;
                        value.Cards.Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()));
                        return value;
                    });
                oldPlayerState.PlayerName = playerState.PlayerName;

                if (playerState.ChannelId == 0 || oldPlayerState.TextChannel != null) continue;
                foreach (var guild in state.Guilds)
                {
                    var getChannelsResult = await guildApi
                        .GetGuildChannelsAsync(guild);
                    if (!getChannelsResult.IsSuccess) continue;
                    var channels = getChannelsResult.Entity;
                    if (!channels.Any()) continue;
                    var textChannel = channels
                        .FirstOrDefault(c => c.ID.Value == playerState.ChannelId);
                    if (textChannel == null) continue;
                    oldPlayerState.TextChannel = textChannel;
                }
            }
            else
            {
                var newPlayerState = new ChinesePokerPlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    Naturals = playerState.Naturals,
                    PlayedCards = playerState.PlayedCards,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null
                };

                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}