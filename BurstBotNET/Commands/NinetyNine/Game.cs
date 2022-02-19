#pragma warning disable CA2252
using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Utilities;
using BurstBotShared.Shared.Models.Localization.NinetyNine.Serializables;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Extensions.Embeds;
using Constants = BurstBotShared.Shared.Constants;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.NinetyNine;

using NinetyNineGame =
    IGame<NinetyNineGameState, RawNinetyNineGameState, NinetyNine, NinetyNinePlayerState, NinetyNineGameProgress,
        NinetyNineInGameRequestType>;

public partial class NinetyNine : NinetyNineGame
{
    private static readonly Dictionary<NinetyNineVariation, int[]> SpecialRanks = new()
    {
        { NinetyNineVariation.Taiwanese, new[] { 4, 5, 10, 11, 12, 13 } },
        { NinetyNineVariation.Icelandic, new[] { 4, 9, 10, 12, 13 } },
        { NinetyNineVariation.Standard, new[] { 4, 9, 10, 13 } },
        { NinetyNineVariation.Bloody, new[] { 1, 4, 5, 7, 8, 9, 10, 11, 12, 13 } }
    };

    public static async Task<bool> HandleProgress(string messageContent, NinetyNineGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawNinetyNineGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null)
                return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult =
                    await HandleProgressChange(
                        deserializedIncomingData,
                        gameState, state, channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            var oldBurstPlayers = new List<ulong>(gameState.BurstPlayers);
            var previousPlayerOrder = gameState.CurrentPlayerOrder;
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
            await SendProgressMessages(gameState, deserializedIncomingData, previousPlayerOrder, oldBurstPlayers,
                deserializedIncomingData.BurstPlayers, state, channelApi, logger);

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

    public static async Task HandleEndingResult(string messageContent, NinetyNineGameState state,
        Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        try
        {
            var endingData = JsonSerializer.Deserialize<NinetyNineInGameResponseEndingData>(messageContent);
            if (endingData == null) return;

            state.Progress = NinetyNineGameProgress.Ending;
            var winner = endingData.Winner;

            if (winner == null) return;

            var localization = localizations.GetLocalization().NinetyNine;

            var title = localization.WinTitle.Replace("{playerName}", winner.PlayerName);

            var description = localization.WinDescription
                .Replace("{playerName}", winner.PlayerName)
                .Replace("{verb}", localization.Won)
                .Replace("{totalRewards}", endingData.TotalRewards.ToString());

            var embed = new Embed(
                title,
                Description: description,
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.NinetyNineLogo),
                Image: new EmbedImage(winner.AvatarUrl));

            foreach (var (pId, player) in state.Players)
            {
                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel!.ID,
                        embeds: new[] { embed });
                if (!sendResult.IsSuccess)
                    logger.LogError(
                        "Failed to broadcast ending result in player {PlayerId}'s channel: {Reason}, inner: {Inner}",
                        pId, sendResult.Error.Message, sendResult.Inner);
            }

            await state.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                0,
                JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
                {
                    GameId = state.GameId,
                    RequestType = NinetyNineInGameRequestType.Close,
                    PlayerId = 0
                })));
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(messageContent)) return;
            Utilities.HandleException(ex, messageContent, state.Semaphore, logger);
        }
    }

    public static async Task<bool> HandleProgressChange(
        RawNinetyNineGameState deserializedIncomingData,
        NinetyNineGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result1 = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerNewState);
            var result2 = gameState.Players.TryGetValue(playerId, out _);
            if (result1 && result2)
            {
                var oldBurstPlayers = new List<ulong>(gameState.BurstPlayers);
                await ShowPreviousPlayerAction(gameState, previousPlayerNewState!,
                    deserializedIncomingData.PreviousCards, gameState.CurrentPlayerOrder, oldBurstPlayers,
                    deserializedIncomingData.BurstPlayers,
                    state, channelApi, logger);
            }
        }

        switch (deserializedIncomingData.Progress)
        {
            case NinetyNineGameProgress.Ending:
                return true;
            case NinetyNineGameProgress.Progressing:
                await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                break;
        }

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);

        return true;
    }

    private static async Task SendProgressMessages(
        NinetyNineGameState gameState,
        RawNinetyNineGameState? deserializedIncomingData,
        int previousPlayerOrder,
        IEnumerable<ulong> oldBurstPlayers,
        IEnumerable<ulong> newBurstPlayers,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (deserializedIncomingData == null) return;

        var getPlayerStateResult = gameState
            .Players
            .TryGetValue(deserializedIncomingData.PreviousPlayerId, out var previousPlayerOldState);
        if (!getPlayerStateResult)
        {
            logger.LogError("Failed to get player {PlayerId}'s old state", deserializedIncomingData.PreviousPlayerId);
            return;
        }

        getPlayerStateResult = deserializedIncomingData
            .Players.TryGetValue(deserializedIncomingData.PreviousPlayerId, out var previousPlayerNewState);
        if (!getPlayerStateResult)
        {
            logger.LogError("Failed to get player {PlayerId}'s new state", deserializedIncomingData.PreviousPlayerId);
            return;
        }

        logger.LogDebug("Sending progress messages...");
        logger.LogDebug("Game progress: {Progress}", gameState.Progress);

        switch (gameState.Progress)
        {
            case NinetyNineGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState!, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case NinetyNineGameProgress.Progressing:
            case NinetyNineGameProgress.Ending:
            {
                var previousCard = deserializedIncomingData.PreviousCards;

                await ShowPreviousPlayerAction(gameState, previousPlayerNewState!,
                    previousCard, previousPlayerOrder, oldBurstPlayers, newBurstPlayers, state, channelApi, logger);

                if (deserializedIncomingData.Progress.Equals(NinetyNineGameProgress.Ending)) return;

                await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);

                break;
            }
        }
    }

    private static async Task SendInitialMessage(
        NinetyNinePlayerState? playerState,
        RawNinetyNinePlayerState newPlayerState,
        RawNinetyNineGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().NinetyNine;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);

        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{variation}", deserializedIncomingData.Variation switch
            {
                NinetyNineVariation.Taiwanese => localization.Taiwanese,
                NinetyNineVariation.Icelandic => localization.Icelandic,
                NinetyNineVariation.Standard => localization.Standard,
                NinetyNineVariation.Bloody => localization.Bloody,
                _ => localization.Taiwanese
            })
            .Replace("{helpText}", deserializedIncomingData.Variation switch
            {
                NinetyNineVariation.Taiwanese => localization.CommandList["generalTaiwanese"],
                NinetyNineVariation.Icelandic => localization.CommandList["generalIcelandic"],
                NinetyNineVariation.Standard => localization.CommandList["generalStandard"],
                NinetyNineVariation.Bloody => localization.CommandList["generalBloody"],
                _ => localization.CommandList["generalTaiwanese"]
            })
            .Replace("{cardNames}", cardNames);

        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);

        var embed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.NinetyNineLogo));

        var attachment = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedDeck))
        };

        var sendResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { embed },
                attachments: attachment);
        if (!sendResult.IsSuccess)
            logger.LogError("Failed to send initial message to player {PlayerId}: {Reason}, inner: {Inner}",
                newPlayerState.PlayerId,
                sendResult.Error.Message, sendResult.Inner);
    }

    private static async Task SendDrawingMessage(
        NinetyNineGameState gameState,
        RawNinetyNineGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = localizations.GetLocalization().NinetyNine;

        var nextPlayer = deserializedIncomingData
            .Players
            .First(pair => pair.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        await using var renderedHiddenImage = nextPlayer.Cards.Count == 0
            ? null
            : SkiaService.RenderDeck(deckService, nextPlayer.Cards.Select(c => c with { IsFront = false }));

        foreach (var (playerId, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null) continue;

            var embed = BuildTurnMessage(playerState, nextPlayer, deserializedIncomingData.CurrentTotal, localizations);

            if (nextPlayer.PlayerId == playerId)
            {
                await using var renderedImage = nextPlayer.Cards.Count == 0
                    ? null
                    : SkiaService.RenderDeck(deckService, nextPlayer.Cards);

                if (renderedImage != null)
                {
                    var attachment = new[]
                    {
                        OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedImage))
                    };
                    
                    var component = BuildComponents(nextPlayer, deserializedIncomingData, localization);

                    if (component == null)
                    {
                        var newComponent = BuildConfirmButton(localization);

                        embed = embed with
                        {
                            Description = localization.GameOver +
                                          $"\n\n{localization.Cards}" +
                                          $"\n\n{string.Join('\n', nextPlayer.Cards)}" +
                                          $"\n\n{localization.CurrentTotal.Replace("{total}", deserializedIncomingData.CurrentTotal.ToString())}",
                            Image = new EmbedImage(Constants.AttachmentUri)
                        };

                        var sendResult = await channelApi
                            .CreateMessageAsync(playerState.TextChannel.ID,
                                embeds: new[] { embed },
                                attachments: attachment,
                                components: newComponent);

                        if (!sendResult.IsSuccess)
                            logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                                playerId, sendResult.Error.Message, sendResult.Inner);
                        continue;
                    }
                    
                    var result = await channelApi
                        .CreateMessageAsync(playerState.TextChannel.ID,
                            embeds: new[] { embed },
                            attachments: attachment,
                            components: component);
                    if (!result.IsSuccess)
                        logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                            playerId, result.Error.Message, result.Inner);

                    continue;
                }

                var confirmComponent = BuildConfirmButton(localization);

                embed = embed with
                {
                    Description = localization.GameOver +
                                  $"\n\n{localization.Cards}" +
                                  $"\n\n{string.Join('\n', nextPlayer.Cards)}" +
                                  $"\n\n{localization.CurrentTotal.Replace("{total}", deserializedIncomingData.CurrentTotal.ToString())}",
                    Image = new EmbedImage(Constants.AttachmentUri)
                };

                var sendConfirmButtonResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: new[] { embed },
                        components: confirmComponent);

                if (!sendConfirmButtonResult.IsSuccess)
                    logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                        playerId, sendConfirmButtonResult.Error.Message, sendConfirmButtonResult.Inner);
            }
            else
            {
                if (renderedHiddenImage != null)
                {
                    var randomFileName = Utilities.GenerateRandomString() + ".jpg";
                    var randomAttachmentUri = $"attachment://{randomFileName}";

                    await using var imageCopy = new MemoryStream((int)renderedHiddenImage.Length);
                    await renderedHiddenImage.CopyToAsync(imageCopy);
                    imageCopy.Seek(0, SeekOrigin.Begin);
                    renderedHiddenImage.Seek(0, SeekOrigin.Begin);

                    embed = embed with
                    {
                        Image = new EmbedImage(randomAttachmentUri)
                    };

                    var attachment = new OneOf<FileData, IPartialAttachment>[]
                    {
                        new FileData(randomFileName, imageCopy)
                    };

                    var sendResult = await channelApi
                        .CreateMessageAsync(playerState.TextChannel.ID,
                            embeds: new[] { embed },
                            attachments: attachment);
                    if (!sendResult.IsSuccess)
                        logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                            playerId, sendResult.Error.Message, sendResult.Inner);
                }
                else
                {
                    var sendResult = await channelApi
                        .CreateMessageAsync(playerState.TextChannel.ID,
                            embeds: new[] { embed });
                    if (!sendResult.IsSuccess)
                        logger.LogError("Failed to send drawing message to player {PlayerId}: {Reason}, inner: {Inner}",
                            playerId, sendResult.Error.Message, sendResult.Inner);
                }
            }
        }
    }

    private static IMessageComponent[]? BuildComponents(
        RawNinetyNinePlayerState currentPlayer,
        RawNinetyNineGameState gameState,
        NinetyNineLocalization localization)
    {
        var customId = gameState.Variation switch
        {
            NinetyNineVariation.Taiwanese => "ninety_nine_help_Taiwanese",
            NinetyNineVariation.Icelandic => "ninety_nine_help_Icelandic",
            NinetyNineVariation.Standard => "ninety_nine_help_Standard",
            NinetyNineVariation.Bloody => "ninety_nine_help_Bloody",
            _ => "ninety_nine_help_Taiwanese"
        };
        var helpButton = new ButtonComponent(ButtonComponentStyle.Primary, localization.ShowHelp,
            new PartialEmoji(Name: "❓"), customId);

        var availableCards = GetAvailableCards(currentPlayer.Cards, gameState, currentPlayer)
            .ToImmutableArray();

        var minValue = 1;
        var maxValue = gameState.ConsecutiveQueens.Count == 0 ? 1 : gameState.ConsecutiveQueens.Count;
        if (gameState.ConsecutiveQueens.Count > 0 && !availableCards.Any(c => c.Number == 12))
            minValue = gameState.ConsecutiveQueens.Count;

        if (availableCards.IsEmpty) return null;

        var options = availableCards
            .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                c.Number != 0 ? new PartialEmoji(c.Suit.ToSnowflake()) : new PartialEmoji(Name: "🃏")))
            .ToImmutableArray();

        var userSelectMenu = new SelectMenuComponent("ninety_nine_user_selection",
            options,
            localization.Play, minValue, maxValue);

        return new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                userSelectMenu
            }),
            new ActionRowComponent(new[]
            {
                helpButton
            })
        };
    }

    private static Embed BuildTurnMessage(
        IPlayerState playerState,
        RawNinetyNinePlayerState nextPlayer,
        ushort currentTotal,
        Localizations localizations)
    {
        var isCurrentPlayer = nextPlayer.PlayerId == playerState.PlayerId;
        var localization = localizations.GetLocalization();

        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
            : localization.GenericWords.PossessiveThird.Replace("{playerName}", nextPlayer.PlayerName);

        var title = localization.NinetyNine.TurnMessageTitle
            .Replace("{possessive}", possessive);

        var embed = new Embed(
            Author: new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.NinetyNineLogo));

        if (!isCurrentPlayer) return embed;

        embed = embed with
        {
            Description =
            $"{localization.NinetyNine.Cards}\n\n{string.Join('\n', nextPlayer.Cards)}\n\n{localization.NinetyNine.CurrentTotal.Replace("{total}", currentTotal.ToString())}",
            Image = new EmbedImage(Constants.AttachmentUri)
        };

        return embed;
    }

    private static async Task ShowPreviousPlayerAction(
        NinetyNineGameState gameState,
        RawNinetyNinePlayerState previousPlayerNewState,
        IReadOnlyCollection<Card>? previousCards,
        int previousPlayerOrder,
        IEnumerable<ulong> oldBurstPlayers,
        IEnumerable<ulong> newBurstPlayers,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (gameState.Progress.Equals(NinetyNineGameProgress.Starting)) return;

        var localization = state.Localizations.GetLocalization();
        var ninetyNineLocalization = localization.NinetyNine;

        var newBurstPlayer = newBurstPlayers
            .Except(oldBurstPlayers)
            .ToImmutableArray();

        foreach (var (pId, player) in gameState.Players)
        {
            if (player.TextChannel == null) continue;

            var isPreviousPlayer = player.Order == previousPlayerOrder;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : previousPlayerNewState.PlayerName;

            if (!newBurstPlayer.IsEmpty)
            {
                var authorText = ninetyNineLocalization.BurstMessage
                    .Replace("{previousPlayerName}", pronoun);

                var embed = new EmbedBuilder()
                    .WithAuthor(authorText, iconUrl: previousPlayerNewState.AvatarUrl)
                    .WithDescription(
                        ninetyNineLocalization.CurrentTotal.Replace("{total}", gameState.CurrentTotal.ToString()))
                    .WithColour(BurstColor.Burst.ToColor())
                    .WithThumbnailUrl(Constants.NinetyNineLogo)
                    .Build();

                var result = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed.Entity });

                if (!result.IsSuccess)
                {
                    logger.LogError("Failed to show previous player {PlayerId}'s action: {Reason}, inner: {Inner}",
                        pId, result.Error.Message, result.Inner);
                }
            }
            else
            {
                if (previousCards == null) return;

                await using var previousCardImage = SkiaService.RenderDeck(state.DeckService, previousCards);

                var description = string.Join('\n', previousCards.Select(c => c.ToString())) +
                                  $"\n\n{ninetyNineLocalization.CurrentTotal.Replace("{total}", gameState.CurrentTotal.ToString())}";

                var authorText = ninetyNineLocalization.PlayMessage
                    .Replace("{previousPlayerName}", pronoun);

                await using var imageCopy = new MemoryStream((int)previousCardImage.Length);
                await previousCardImage.CopyToAsync(imageCopy);
                previousCardImage.Seek(0, SeekOrigin.Begin);
                imageCopy.Seek(0, SeekOrigin.Begin);

                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, imageCopy))
                };

                var embed = new Embed(
                    Author: new EmbedAuthor(authorText, IconUrl: previousPlayerNewState.AvatarUrl),
                    Description: description,
                    Colour: BurstColor.Burst.ToColor(),
                    Image: new EmbedImage(Constants.AttachmentUri));

                var result = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment);

                if (!result.IsSuccess)
                {
                    logger.LogError("Failed to show previous player {PlayerId}'s action: {Reason}, inner: {Inner}",
                        pId, result.Error.Message, result.Inner);
                }
            }
        }
    }

    private static async Task UpdateGameState(
        NinetyNineGameState state,
        RawNinetyNineGameState? data,
        IDiscordRestGuildAPI guildApi)
    {
        if (data == null)
            return;
        state.CurrentTotal = data.CurrentTotal;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;
        state.PreviousCards = data.PreviousCards?.ToImmutableArray() ?? ImmutableArray<Card>.Empty;
        state.BaseBet = data.BaseBet;
        state.Difficulty = data.Difficulty;
        state.TotalBet = data.TotalBet;
        state.Variation = data.Variation;
        state.BurstPlayers = data.BurstPlayers.ToImmutableArray();

        foreach (var (playerId, playerState) in data.Players)
        {
            playerState.Cards.Sort((a, b) => a.Number.CompareTo(b.Number));
            if (state.Players.ContainsKey(playerId))
            {
                var oldPlayerState = state.Players.GetOrAdd(playerId, new NinetyNinePlayerState());
                oldPlayerState.Cards = playerState.Cards.ToImmutableArray()
                    .Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()));
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayerName = playerState.PlayerName;
                oldPlayerState.Order = playerState.Order;
                oldPlayerState.IsDrawn = playerState.IsDrawn;
                oldPlayerState.PassTimes = playerState.PassTimes;

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
                var newPlayerState = new NinetyNinePlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    Order = playerState.Order,
                    TextChannel = null
                };
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }

    private static IEnumerable<Card> GetAvailableCards(
        IEnumerable<Card> cards,
        RawNinetyNineGameState gameState,
        RawNinetyNinePlayerState playerState)
    {
        var variation = gameState.Variation;
        var currentTotal = gameState.CurrentTotal;
        var previousCards = gameState.PreviousCards;

        switch (variation)
        {
            case NinetyNineVariation.Taiwanese:
                return cards.Where(c =>
                    SpecialRanks[NinetyNineVariation.Taiwanese].Contains(c.Number) ||
                    c.Suit == Suit.Spade && c.Number == 1 || currentTotal + c.Number <= 99);
            case NinetyNineVariation.Icelandic:
            {
                if (previousCards == null || previousCards.Last().Number != 12)
                {
                    return cards.Where(c =>
                        SpecialRanks[NinetyNineVariation.Icelandic].Contains(c.Number) ||
                        currentTotal + c.Number <= 99);
                }

                var cardArr = cards.ToImmutableArray();
                var queens = cardArr.Where(c => c.Number == 12);
                var nonQueens = cardArr
                    .Where(c => SpecialRanks[NinetyNineVariation.Icelandic].Contains(c.Number) ||
                                currentTotal + c.Number <= 99)
                    .Where(c => c.Number != 12)
                    .ToImmutableArray();

                return nonQueens.Length >= gameState.ConsecutiveQueens.Count ? queens.Concat(nonQueens) : queens;
            }
            case NinetyNineVariation.Standard:
            {
                var cardsArr = cards.ToImmutableArray();
                var availableCards = cardsArr
                    .Where(c =>
                        (SpecialRanks[variation].Contains(c.Number) ||
                         currentTotal + c.Number <= 99) && c.Number != 11 && c.Number != 12).ToList();
                var jackAndQueens = cardsArr.Where(c => c.Number is 11 or 12);
                if (currentTotal + 10 <= 99)
                    availableCards.AddRange(jackAndQueens);

                return availableCards;
            }
            case NinetyNineVariation.Bloody:
            {
                var availableCards = cards.Where(c =>
                        SpecialRanks[variation].Contains(c.Number) ||
                        currentTotal + c.Number <= 99)
                    .ToImmutableArray();
                var availablePlayers = gameState
                    .Players
                    .Where(p =>
                    {
                        var (pId, pState) = p;
                        var notBurst = !gameState.BurstPlayers.Contains(pId);
                        var isNotPassing = pState.PassTimes == 0;
                        var notSelf = pId != playerState.PlayerId;
                        return notBurst && isNotPassing && notSelf;
                    })
                    .Select(p => p.Key)
                    .ToImmutableArray();

                return availablePlayers.IsEmpty ? availableCards.Where(c => c.Number != 5) : availableCards;
            }
            default:
                return Enumerable.Empty<Card>();
        }
    }

    private static IMessageComponent[] BuildConfirmButton(NinetyNineLocalization localization)
    {
        var confirmButton = new ButtonComponent(ButtonComponentStyle.Success, localization.Confirm,
            new PartialEmoji(Name: "😫"), "confirm");

        return new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                confirmButton
            })
        };
    }
}