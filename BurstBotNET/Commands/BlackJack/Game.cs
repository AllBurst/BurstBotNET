using System.Collections.Immutable;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Rest.Core;
using Remora.Results;
using Channel = System.Threading.Channels.Channel;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.BlackJack;

using BlackJackGame =
    IGame<BlackJackGameState, RawBlackJackGameState, BlackJack, BlackJackPlayerState, BlackJackGameProgress,
        BlackJackInGameRequestType>;

#pragma warning disable CA2252
public partial class BlackJack : BlackJackGame
{
    public static async Task<bool> HandleProgress(
        string messageContent, BlackJackGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawBlackJackGameState>(messageContent, Game.JsonSerializerSettings);
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

            var previousHighestBet = gameState.HighestBet;
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result = gameState.Players.TryGetValue(playerId, out var previousPlayerState);
            if (!result)
            {
                gameState.Semaphore.Release();
                return false;
            }

            result = Enum.TryParse<BlackJackInGameRequestType>(deserializedIncomingData.PreviousRequestType,
                out var previousRequestType);
            if (!result)
            {
                gameState.Semaphore.Release();
                return false;
            }

            await SendProgressMessages(gameState, previousPlayerState, previousRequestType, previousHighestBet,
                deserializedIncomingData, state.DeckService, state.Localizations, channelApi, logger);

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

    public static async Task HandleEndingResult(
        string messageContent,
        BlackJackGameState state,
        Localizations localizations,
        DeckService deckService,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (state.Progress.Equals(BlackJackGameProgress.Ending))
            return;

        logger.LogDebug("Handling ending result...");

        try
        {
            var deserializedEndingData = JsonSerializer.Deserialize<BlackJackInGameResponseEndingData>(messageContent);
            if (deserializedEndingData == null)
                return;

            state.Progress = deserializedEndingData.Progress;
            var result = state.Players.TryGetValue(deserializedEndingData.Winner?.PlayerId ?? 0, out var winner);
            if (!result)
                return;

            var localization = localizations.GetLocalization().BlackJack;

            var description = localization.WinDescription
                .Replace("{playerName}", winner!.PlayerName)
                .Replace("{totalRewards}", deserializedEndingData.TotalRewards.ToString());

            var fields = new List<EmbedField>(state.Players.Count);

            foreach (var (_, playerState) in deserializedEndingData.Players)
            {
                var cardNames = string.Join('\n', playerState.Cards.Select(c => c.ToString()));
                var totalPoints = playerState.Cards.GetRealizedValues(100);
                fields.Add(new EmbedField(playerState.PlayerName, localization.TotalPointsMessage
                    .Replace("{cardNames}", cardNames)
                    .Replace("{totalPoints}", totalPoints.ToString()), true));
            }

            var embed = new Embed(
                Colour: BurstColor.Burst.ToColor(),
                Title: localization.WinTitle.Replace("{playerName}", winner.PlayerName),
                Description: description,
                Image: new EmbedImage(winner.AvatarUrl),
                Fields: fields);

            foreach (var (_, playerState) in state.Players)
            {
                if (playerState.TextChannel == null)
                    continue;

                var sendEmbedResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: new[] { embed });
                if (!sendEmbedResult.IsSuccess)
                    logger.LogError("Failed to send ending result to player's channel: {Reason}, inner: {Inner}",
                        sendEmbedResult.Error.Message, sendEmbedResult.Inner);
            }

            await state.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(0, JsonSerializer.SerializeToUtf8Bytes(
                new BlackJackInGameRequest
                {
                    RequestType = BlackJackInGameRequestType.Close,
                    GameId = state.GameId,
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

    public static async Task AddPlayerState(string gameId,
        Snowflake guild,
        BlackJackPlayerState playerState,
        GameStates gameStates)
    {
        var state = gameStates.BlackJackGameStates.Item1.GetOrAdd(gameId, new BlackJackGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.BlackJackGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new BlackJackInGameRequest
            {
                GameId = gameId,
                AvatarUrl = playerState.AvatarUrl,
                PlayerId = playerState.PlayerId,
                ChannelId = playerState.TextChannel.ID.Value,
                PlayerName = playerState.PlayerName,
                OwnTips = playerState.OwnTips,
                ClientType = ClientType.Discord,
                RequestType = BlackJackInGameRequestType.Deal,
            })
        ));
    }

    public static async Task HandleBlackJackMessage(
        IMessageCreate gatewayEvent,
        GameStates gameStates,
        Snowflake channelId,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var state = gameStates
            .BlackJackGameStates
            .Item1
            .Where(pair => !pair.Value.Players
                .Where(p => p.Value.TextChannel?.ID.Value == channelId.Value)
                .ToImmutableArray().IsEmpty)
            .Select(p => p.Value)
            .First();

        var playerState = state
            .Players
            .Where(p => p.Value.TextChannel?.ID.Value == channelId.Value)
            .Select(p => p.Value)
            .First();

        // Do not respond if the one who's typing is not the owner of the channel.
        if (!gatewayEvent.Author.ID.Value.Equals(playerState.PlayerId))
            return;

        if (!playerState.IsRaising) return;

        playerState.IsRaising = false;

        var sanitizedMessage = gatewayEvent.Content.Trim();
        var parseResult = int.TryParse(sanitizedMessage, out var raiseBet);
        var localization = localizations.GetLocalization().BlackJack;
        if (!parseResult)
        {
            var result = await channelApi
                .CreateMessageAsync(playerState.TextChannel!.ID,
                    localization.RaiseInvalidNumber);
            if (!result.IsSuccess)
                logger.LogError("Failed to send invalid raise message to the channel: {Reason}, inner: {Inner}",
                    result.Error.Message, result.Inner);
            return;
        }

        if (playerState.BetTips + raiseBet > playerState.OwnTips)
        {
            var result = await channelApi
                .CreateMessageAsync(playerState.TextChannel!.ID,
                    localization.RaiseExcessiveNumber);
            if (!result.IsSuccess)
                logger.LogError("Failed to send invalid raise message to the channel: {Reason}, inner: {Inner}",
                    result.Error.Message, result.Inner);
            return;
        }

        await BlackJackButtonEntity.SendRaiseData(state, playerState, raiseBet, channelApi, logger);
    }

    public static async Task<bool> HandleProgressChange(
        RawBlackJackGameState deserializedIncomingData,
        BlackJackGameState gameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        {
            var playerId = deserializedIncomingData.PreviousPlayerId;
            var result = deserializedIncomingData.Players.TryGetValue(playerId, out var previousPlayerState);
            if (result)
            {
                result = Enum.TryParse<BlackJackInGameRequestType>(deserializedIncomingData.PreviousRequestType,
                    out var previousRequestType);
                if (result)
                    await SendPreviousPlayerActionMessage(gameState, previousPlayerState!,
                        previousRequestType, state.DeckService, state.Localizations,
                        channelApi, logger,
                        gameState.HighestBet);
            }
        }

        if (deserializedIncomingData.Progress.Equals(BlackJackGameProgress.Ending))
            return true;

        gameState.Progress = deserializedIncomingData.Progress;
        await UpdateGameState(gameState, deserializedIncomingData, guildApi);
        var firstPlayer = gameState.Players
            .First(pair => pair.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        switch (deserializedIncomingData.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                foreach (var playerState in gameState.Players)
                {
                    if (playerState.Value.TextChannel == null)
                        continue;

                    state.GameStates.BlackJackGameStates.Item2.Add(playerState.Value.TextChannel.ID);
                    var turnResult = await BuildTurnMessage(
                        playerState,
                        deserializedIncomingData.CurrentPlayerOrder,
                        firstPlayer,
                        gameState,
                        state.DeckService,
                        state.Localizations,
                        channelApi
                    );

                    if (!turnResult.IsSuccess)
                        logger.LogError("Failed to send turn message: {Reason}, inner: {Inner}",
                            turnResult.Error.Message, turnResult.Inner);
                }

                break;
            }
            case BlackJackGameProgress.Gambling:
            {
                foreach (var playerState in gameState.Players)
                {
                    if (playerState.Value.TextChannel == null)
                        continue;

                    var gamblingStartMessageResult = await channelApi
                        .CreateMessageAsync(playerState.Value.TextChannel.ID,
                            state.Localizations.GetLocalization().BlackJack
                                .GamblingInitialMessage);

                    if (!gamblingStartMessageResult.IsSuccess)
                        logger.LogError("Failed to send gambling start message: {Reason}, inner: {Inner}",
                            gamblingStartMessageResult.Error.Message, gamblingStartMessageResult.Inner);

                    var turnResult = await BuildTurnMessage(
                        playerState,
                        deserializedIncomingData.CurrentPlayerOrder,
                        firstPlayer,
                        gameState,
                        state.DeckService,
                        state.Localizations,
                        channelApi
                    );

                    if (!turnResult.IsSuccess)
                        logger.LogError("Failed to send turn message: {Reason}, inner: {Inner}",
                            turnResult.Error.Message, turnResult.Inner);
                }

                break;
            }
        }

        return true;
    }

    private static async Task SendProgressMessages(
        BlackJackGameState state,
        BlackJackPlayerState? previousPlayerState,
        BlackJackInGameRequestType previousRequestType,
        int previousHighestBet,
        RawBlackJackGameState? deserializedStateData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (deserializedStateData == null)
            return;

        logger.LogDebug("Sending progress messages...");

        switch (state.Progress)
        {
            case BlackJackGameProgress.Starting:
                await SendInitialMessage(previousPlayerState, deckService, localizations, channelApi, logger);
                break;
            case BlackJackGameProgress.Progressing:
                await SendDrawingMessage(
                    state,
                    previousPlayerState,
                    state.CurrentPlayerOrder,
                    previousRequestType,
                    deserializedStateData.Progress,
                    deckService,
                    localizations,
                    channelApi,
                    logger
                );
                break;
            case BlackJackGameProgress.Gambling:
                await SendGamblingMessage(
                    state, previousPlayerState, state.CurrentPlayerOrder, previousRequestType, previousHighestBet,
                    deserializedStateData.Progress, deckService, localizations, channelApi, logger);
                break;
        }
    }

    private static async Task SendInitialMessage(
        BlackJackPlayerState? playerState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (playerState?.TextChannel == null)
            return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().BlackJack;
        var prefix = localization.InitialMessagePrefix;
        var postfix = localization.InitialMessagePostfix
            .Replace("{cardPoints}", playerState.Cards.GetRealizedValues(100).ToString());
        var cardNames = prefix +
                        string.Join('\n', playerState.Cards.Select(c => c.IsFront ? c.ToString() : $"**{c}**")) +
                        postfix;

        var description = localization.InitialMessageDescription
            .Replace("{cardsNames}", cardNames);

        var attachments = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, SkiaService.RenderDeck(
                deckService,
                playerState.Cards.Select(c => c with { IsFront = true }))))
        };

        var embed = new Embed(
            Author: new EmbedAuthor(playerState.PlayerName, IconUrl: playerState.AvatarUrl),
            Colour: BurstColor.Burst.ToColor(),
            Title: localization.InitialMessageTitle,
            Description: description,
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
            Image: new EmbedImage(Constants.AttachmentUri));

        var initialMessageResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { embed },
                attachments: attachments);
        if (!initialMessageResult.IsSuccess)
            logger.LogError("Failed to send initial message: {Reason}, inner: {Inner}",
                initialMessageResult.Error.Message, initialMessageResult.Inner);
    }

    private static async Task SendDrawingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? previousPlayerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType previousRequestType,
        BlackJackGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (previousPlayerState == null)
            return;

        await SendPreviousPlayerActionMessage(gameState,
            ((IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>)previousPlayerState).ToRaw(),
            previousRequestType,
            deckService,
            localizations,
            channelApi,
            logger);

        if (!gameState.Progress.Equals(nextProgress))
            return;

        var nextPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == currentPlayerOrder)
            .Value;

        foreach (var state in gameState.Players)
        {
            if (state.Value.TextChannel == null)
                continue;

            var turnResult = await BuildTurnMessage(state, currentPlayerOrder, nextPlayer, gameState,
                deckService, localizations, channelApi);
            if (!turnResult.IsSuccess)
                logger.LogError("Failed to send turn message: {Reason}, inner: {Inner}",
                    turnResult.Error.Message, turnResult.Inner);
        }
    }

    private static async Task SendGamblingMessage(
        BlackJackGameState gameState,
        BlackJackPlayerState? previousPlayerState,
        int currentPlayerOrder,
        BlackJackInGameRequestType previousRequestType,
        int previousHighestBet,
        BlackJackGameProgress nextProgress,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (previousPlayerState == null)
            return;

        await SendPreviousPlayerActionMessage(gameState,
            ((IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>)previousPlayerState).ToRaw(),
            previousRequestType,
            deckService, localizations,
            channelApi, logger,
            previousHighestBet);

        if (gameState.Progress != nextProgress)
            return;

        var currentPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == currentPlayerOrder)
            .Value;
        foreach (var state in gameState.Players)
        {
            if (state.Value.TextChannel == null)
                continue;

            var turnResult = await BuildTurnMessage(state, currentPlayerOrder, currentPlayer, gameState,
                deckService, localizations, channelApi);
            if (!turnResult.IsSuccess)
                logger.LogError("Failed to send turn message: {Reason}, inner: {Inner}",
                    turnResult.Error.Message, turnResult.Inner);
        }
    }

    private static async Task SendPreviousPlayerActionMessage(
        BlackJackGameState gameState,
        RawBlackJackPlayerState previousPlayerState,
        BlackJackInGameRequestType previousRequestType,
        DeckService deck,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger,
        int? previousHighestBet = null)
    {
        var previousPlayerOrder = previousPlayerState.Order;
        var localization = localizations.GetLocalization();
        var lastCard = previousPlayerState.Cards.Last();
        var lastCardImage = SkiaService.RenderCard(deck, lastCard);

        foreach (var (_, state) in gameState.Players)
        {
            if (state.TextChannel == null)
                continue;

            var isPreviousPlayer = previousPlayerOrder == state.Order;
            var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : previousPlayerState.PlayerName;
            var currentPoints = previousPlayerState.Cards.GetRealizedValues(100);

            switch (gameState.Progress)
            {
                case BlackJackGameProgress.Progressing:
                {
                    var authorText = BuildPlayerActionMessage(localizations, previousRequestType, pronoun, lastCard);

                    var embed = new Embed(
                        Author: new EmbedAuthor(authorText, IconUrl: previousPlayerState.AvatarUrl),
                        Colour: BurstColor.Burst.ToColor());

                    if (previousRequestType.Equals(BlackJackInGameRequestType.Draw))
                        embed = embed with { Image = new EmbedImage(Constants.AttachmentUri) };

                    if (isPreviousPlayer)
                        embed = embed with
                        {
                            Description =
                            localization.BlackJack.CardPoints.Replace("{cardPoints}", currentPoints.ToString())
                        };

                    await using var lastCardImageCopy = new MemoryStream((int)lastCardImage.Length);
                    await lastCardImage.CopyToAsync(lastCardImageCopy);
                    lastCardImage.Seek(0, SeekOrigin.Begin);
                    lastCardImageCopy.Seek(0, SeekOrigin.Begin);

                    var result = await channelApi
                        .CreateMessageAsync(state.TextChannel.ID,
                            embeds: new[] { embed },
                            attachments: previousRequestType.Equals(BlackJackInGameRequestType.Draw)
                                ? new[]
                                {
                                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName,
                                        lastCardImageCopy))
                                }
                                : Array.Empty<OneOf<FileData, IPartialAttachment>>());

                    if (!result.IsSuccess)
                        logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                            result.Error.Message, result.Inner);

                    break;
                }
                case BlackJackGameProgress.Gambling:
                {
                    var verb = isPreviousPlayer
                        ? localization.GenericWords.ParticipateSecond
                        : localization.GenericWords.ParticipateThird;

                    var authorText = BuildPlayerActionMessage(
                        localizations, previousRequestType, pronoun,
                        highestBet: gameState.HighestBet,
                        verb: verb,
                        diff: gameState.HighestBet - previousHighestBet!.Value);

                    var embed = new Embed(
                        Author: new EmbedAuthor(authorText, IconUrl: previousPlayerState.AvatarUrl),
                        Colour: BurstColor.Burst.ToColor());

                    if (isPreviousPlayer)
                        embed = embed with
                        {
                            Description = localization.BlackJack.CardPoints.Replace(
                                "{cardPoints}",
                                currentPoints.ToString()
                            )
                        };

                    var result = await channelApi
                        .CreateMessageAsync(state.TextChannel.ID,
                            embeds: new[] { embed });

                    if (!result.IsSuccess)
                        logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                            result.Error.Message, result.Inner);

                    break;
                }
            }
        }
    }

    private static string BuildPlayerActionMessage(
        Localizations localizations,
        BlackJackInGameRequestType requestType,
        string playerName,
        Card? lastCard = null,
        int? highestBet = null,
        string? verb = null,
        int? diff = null
    )
    {
        var localization = localizations.GetLocalization().BlackJack;
        return requestType switch
        {
            BlackJackInGameRequestType.Draw => localization.DrawMessage
                .Replace("{playerName}", playerName)
                .Replace("{lastCard}", lastCard!.ToStringSimple()),
            BlackJackInGameRequestType.Stand => localization.StandMessage
                .Replace("{playerName}", playerName),
            BlackJackInGameRequestType.Call => localization.CallMessage
                .Replace("{playerName}", playerName)
                .Replace("{highestBet}", highestBet.ToString()),
            BlackJackInGameRequestType.Fold => localization.FoldMessage
                .Replace("{playerName}", playerName)
                .Replace("{verb}", verb),
            BlackJackInGameRequestType.Raise => localization.RaiseMessage
                .Replace("{playerName}", playerName)
                .Replace("{diff}", diff.ToString())
                .Replace("{highestBet}", highestBet.ToString()),
            BlackJackInGameRequestType.AllIn => localization.AllinMessage
                .Replace("{playerName}", playerName)
                .Replace("{highestBet}", highestBet.ToString()),
            _ => localization.UnknownMessage
                .Replace("{playerName}", playerName)
        };
    }

    private static async Task<Result> BuildTurnMessage(
        KeyValuePair<ulong, BlackJackPlayerState> entry,
        int currentPlayerOrder,
        BlackJackPlayerState currentPlayer,
        BlackJackGameState gameState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi)
    {
        var localization = localizations.GetLocalization();
        var playerState = entry.Value;
        var isCurrentPlayer = playerState.Order == currentPlayerOrder;

        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond
            : localization.GenericWords.PossessiveThird
                .Replace("{playerName}", currentPlayer.PlayerName);

        var cardNames = $"{possessive}{localization.GenericWords.Card}:\n" + string.Join('\n', currentPlayer.Cards
            .Where(c => isCurrentPlayer || c.IsFront)
            .Select(c => c.IsFront ? c.ToString() : $"**{c}**"));

        var title = localization.BlackJack.TurnMessageTitle
            .Replace("{possessive}", isCurrentPlayer ? possessive.ToLowerInvariant() : possessive);

        var description = cardNames + (isCurrentPlayer
            ? "\n\n" + localization.BlackJack.CardPoints
                .Replace("{cardPoints}", playerState.Cards.GetRealizedValues(100).ToString())
            : "");

        var embed = new Embed(
            Author: new EmbedAuthor(currentPlayer.PlayerName, IconUrl: currentPlayer.AvatarUrl),
            Colour: BurstColor.Burst.ToColor(),
            Title: title,
            Image: new EmbedImage(Constants.AttachmentUri));

        var attachments = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, SkiaService.RenderDeck(
                deckService,
                currentPlayer.Cards.Select(c => isCurrentPlayer ? c with { IsFront = true } : c))))
        };

        switch (gameState.Progress)
        {
            case BlackJackGameProgress.Progressing:
            {
                if (isCurrentPlayer)
                    embed = embed with { Footer = new EmbedFooter(localization.BlackJack.ProgressingFooter) };
                embed = embed with { Description = description };

                var components = new IMessageComponent[]
                {
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.Draw,
                            new PartialEmoji(Name: "🎴"), "draw"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.Stand,
                            new PartialEmoji(Name: "😑"), "stand"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.ShowHelp,
                            new PartialEmoji(Name: "❓"), "blackjack_help")
                    })
                }.ToImmutableArray();

                var result = await channelApi
                    .CreateMessageAsync(playerState.TextChannel!.ID,
                        embeds: new[] { embed },
                        components: isCurrentPlayer ? components : ImmutableArray<IMessageComponent>.Empty,
                        attachments: attachments);

                return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
            }
            case BlackJackGameProgress.Gambling:
            {
                var additionalDescription =
                    description + (isCurrentPlayer ? localization.BlackJack.TurnMessageDescription : "");
                embed = embed with
                {
                    Fields = new[]
                    {
                        new EmbedField(localization.BlackJack.HighestBets, gameState.HighestBet.ToString(), true),
                        new EmbedField(localization.BlackJack.YourBets, playerState.BetTips.ToString(), true),
                        new EmbedField(localization.BlackJack.TipsBeforeGame, playerState.OwnTips.ToString())
                    },
                    Description = additionalDescription
                };

                var components = new IMessageComponent[]
                {
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.Call,
                            new PartialEmoji(Name: "🤔"), "call"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.Fold,
                            new PartialEmoji(Name: "😫"), "fold"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.Raise,
                            new PartialEmoji(Name: "🤑"), "raise"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.AllIn,
                            new PartialEmoji(Name: "😈"), "allin"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.BlackJack.ShowHelp,
                            new PartialEmoji(Name: "❓"), "blackjack_help")
                    })
                }.ToImmutableArray();

                var result = await channelApi
                    .CreateMessageAsync(playerState.TextChannel!.ID,
                        embeds: new[] { embed },
                        components: isCurrentPlayer ? components : ImmutableArray<IMessageComponent>.Empty,
                        attachments: attachments);

                return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
            }
        }

        return Result.FromSuccess();
    }

    private static async Task UpdateGameState(
        BlackJackGameState state,
        RawBlackJackGameState? data,
        IDiscordRestGuildAPI guildApi)
    {
        if (data == null)
            return;

        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.CurrentTurn = data.CurrentTurn;
        state.HighestBet = data.HighestBet;
        state.PreviousPlayerId = data.PreviousPlayerId;
        state.PreviousRequestType = data.PreviousRequestType;
        state.BaseBet = data.BaseBet;

        foreach (var (playerId, playerState) in data.Players)
            if (state.Players.ContainsKey(playerId))
            {
                var player = state.Players[playerId];
                player.BetTips = playerState.BetTips;
                player.Cards = playerState.Cards.ToImmutableArray();
                player.Order = playerState.Order;
                player.PlayerName = playerState.PlayerName;
                player.OwnTips = playerState.OwnTips;
                player.AvatarUrl = playerState.AvatarUrl;

                if (playerState.ChannelId == 0 || player.TextChannel != null) continue;

                foreach (var guild in state.Guilds)
                {
                    var getChannelsResult = await guildApi
                        .GetGuildChannelsAsync(guild);
                    if (!getChannelsResult.IsSuccess) continue;
                    var channels = getChannelsResult.Entity;
                    if (!channels.Any()) continue;
                    var textChannel = channels.FirstOrDefault(c => c.ID.Value == playerState.ChannelId);
                    if (textChannel == null)
                        continue;
                    player.TextChannel = textChannel;
                }
            }
            else
            {
                var newPlayerState = new BlackJackPlayerState
                {
                    GameId = playerState.GameId,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null,
                    OwnTips = playerState.OwnTips,
                    BetTips = playerState.BetTips,
                    Order = playerState.Order,
                    Cards = playerState.Cards.ToImmutableArray(),
                    AvatarUrl = playerState.AvatarUrl
                };
                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
    }
}