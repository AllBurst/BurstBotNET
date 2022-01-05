using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.OldMaid.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Rest.Core;
using Channel = System.Threading.Channels.Channel;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurstBotNET.Commands.OldMaid;

using OldMaidGame = IGame<OldMaidGameState, RawOldMaidGameState, OldMaid, OldMaidPlayerState, OldMaidGameProgress, OldMaidInGameRequestType>;

public partial class OldMaid : OldMaidGame
{
    private static readonly ImmutableArray<string> InGameRequestTypes =
        Enum.GetNames<OldMaidInGameRequestType>()
            .ToImmutableArray();
    
    public static async Task AddPlayerState(string gameId, Snowflake guild, OldMaidPlayerState playerState, GameStates gameStates)
    {
        var state = gameStates.OldMaidGameStates.Item1
            .GetOrAdd(gameId, new OldMaidGameState());
        state.Players.GetOrAdd(playerState.PlayerId, playerState);
        state.Guilds.Add(guild);
        state.Channel ??= Channel.CreateUnbounded<Tuple<ulong, byte[]>>();

        if (playerState.TextChannel == null)
            return;

        gameStates.OldMaidGameStates.Item2.Add(playerState.TextChannel.ID);
        await state.Channel.Writer.WriteAsync(new Tuple<ulong, byte[]>(playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new OldMaidInGameRequest
            {
                AvatarUrl = playerState.AvatarUrl,
                ChannelId = playerState.TextChannel.ID.Value,
                ClientType = ClientType.Discord,
                GameId = gameId,
                PlayerId = playerState.PlayerId,
                PlayerName = playerState.PlayerName,
                RequestType = OldMaidInGameRequestType.Deal
            })));
    }

    public static async Task<bool> HandleProgress(string messageContent, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData = JsonConvert
                .DeserializeObject<RawOldMaidGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null) return false;

            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (!gameState.Progress.Equals(deserializedIncomingData.Progress))
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData,
                    gameState, state, channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await SendProgressMessages(gameState, deserializedIncomingData, state, channelApi, logger);
            await UpdateGameState(gameState, deserializedIncomingData, guildApi);
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

    public static Task<bool> HandleProgressChange(RawOldMaidGameState deserializedIncomingData, OldMaidGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, OldMaidGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    private static async Task SendProgressMessages(
        OldMaidGameState gameState,
        RawOldMaidGameState? deserializedIncomingData,
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

        switch (gameState.Progress)
        {
            case OldMaidGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState!, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case OldMaidGameProgress.Progressing:
            case OldMaidGameProgress.Ending:
            {
                getPlayerStateResult = deserializedIncomingData
                    .Players.TryGetValue(gameState.PreviousPlayerId, out var ppPlayerState);
                if (!getPlayerStateResult)
                {
                    logger.LogError("Failed to get second previous player {PlayerId}'s state",
                        gameState.PreviousPlayerId);
                    return;
                }
        
                var newDumpedCards = deserializedIncomingData.DumpedCards;

                await ShowPreviousPlayerAction(previousPlayerNewState!, ppPlayerState!.PlayerName,
                    newDumpedCards, gameState, deserializedIncomingData, state, channelApi, logger);

                if (deserializedIncomingData.Progress.Equals(OldMaidGameProgress.Ending)) return;

                await SendDrawingMessage(gameState, deserializedIncomingData, state.DeckService,
                    state.Localizations, channelApi, logger);
                
                break;
            }
        }
    }

    private static async Task SendInitialMessage(
        OldMaidPlayerState? playerState,
        RawOldMaidPlayerState newPlayerState,
        RawOldMaidGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().OldMaid;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);

        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{helpText}", localization.CommandList["draw"])
            .Replace("{cardNames}", cardNames);
        
        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);

        var embed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

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
        OldMaidGameState gameState,
        RawOldMaidGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
        )
    {
        var localization = localizations.GetLocalization().OldMaid;
        
        var nextPlayer = gameState
            .Players
            .First(pair => pair.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        foreach (var (playerId, playerState) in gameState.Players)
        {
            if (playerState.TextChannel == null) continue;

            var embed = BuildDrawingMessage(playerState, nextPlayer, localizations);

            if (nextPlayer.PlayerId == playerId)
            {
                await using var renderedImage = SkiaService.RenderDeck(deckService, nextPlayer.Cards);
                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedImage))
                };

                var components = new IMessageComponent[]
                {
                    new ActionRowComponent(new[]
                    {
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.Draw,
                            new PartialEmoji(Name: "🃏"), "old_maid_draw"),
                        new ButtonComponent(ButtonComponentStyle.Primary, localization.ShowHelp,
                            new PartialEmoji(Name: "❓"), "old_maid_help")
                    })
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(playerState.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment,
                        components: components);
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

    private static Embed BuildDrawingMessage(
        OldMaidPlayerState playerState,
        OldMaidPlayerState nextPlayer,
        Localizations localizations)
    {
        var isCurrentPlayer = nextPlayer.PlayerId == playerState.PlayerId;
        var localization = localizations.GetLocalization();

        var possessive = isCurrentPlayer
            ? localization.GenericWords.PossessiveSecond
            : localization.GenericWords.PossessiveThird.Replace("{playerName}", nextPlayer.PlayerName);

        var title = localization.OldMaid.TurnMessageTitle
            .Replace("{possessive}", possessive);

        var embed = new Embed(
            Author: new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

        if (isCurrentPlayer)
        {
            embed = embed with
            {
                Description = $"{localization.OldMaid.Cards}\n\n{string.Join('\n', nextPlayer.Cards)}",
                Image = new EmbedImage(Constants.AttachmentUri)
            };
        }

        return embed;
    }

    private static async Task ShowPreviousPlayerAction(
        RawOldMaidPlayerState previousPlayerNewState,
        string previousPreviousPlayerName,
        IEnumerable<Card> newDumpedCards,
        OldMaidGameState oldGameState,
        RawOldMaidGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (oldGameState.Progress.Equals(OldMaidGameProgress.Starting)) return;
        
        var localization = state.Localizations.GetLocalization();
        var oldMaidLocalization = state.Localizations.GetLocalization().OldMaid;
        
        var diff = newDumpedCards
            .ExceptBy(oldGameState.DumpedCards.Select(c => c.Number), card => card.Number,
                EqualityComparer<int>.Default)
            .ToImmutableArray();

        var isPreviousPlayer = previousPlayerNewState.Order == oldGameState.CurrentPlayerOrder;

        var pronoun = isPreviousPlayer ? localization.GenericWords.Pronoun : previousPlayerNewState.PlayerName;

        var title = oldMaidLocalization
            .DrawMessage
            .Replace("{previousPlayerName}", pronoun)
            .Replace("{ppPlayerName}", previousPreviousPlayerName)
            .Replace("{card}", diff.IsEmpty ? "card" : newGameState.PreviouslyDrawnCard!.ToString());

        var embed = new Embed(
            Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
            Title: title
        );

        if (!diff.IsEmpty)
        {
            embed = embed with
            {
                Image = new EmbedImage(Constants.AttachmentUri),
                Description = oldMaidLocalization.ThrowMessage
                    .Replace("{previousPlayerName}", pronoun)
                    .Replace("{rank}", newGameState.PreviouslyDrawnCard!.Number.ToString())
            };
            
            await using var renderedImage = SkiaService.RenderDeck(state.DeckService, diff);

            foreach (var (_, player) in oldGameState.Players)
            {
                if (player.TextChannel == null) continue;
                
                await using var streamCopy = new MemoryStream((int)renderedImage.Length);
                await renderedImage.CopyToAsync(streamCopy);
                streamCopy.Seek(0, SeekOrigin.Begin);
                renderedImage.Seek(0, SeekOrigin.Begin);
                
                var attachment = new[]
                {
                    OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, streamCopy))
                };
                
                var sentResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed },
                        attachments: attachment);
                
                if (!sentResult.IsSuccess)
                    logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                        sentResult.Error.Message, sentResult.Inner);
            }
        }
        else
        {
            foreach (var (_, player) in oldGameState.Players)
            {
                var sentResult = await channelApi
                    .CreateMessageAsync(player.TextChannel!.ID,
                        embeds: new[] { embed });
                
                if (!sentResult.IsSuccess)
                    logger.LogError("Failed to show previous player's action: {Reason}, inner: {Inner}",
                        sentResult.Error.Message, sentResult.Inner);
            }
        }
    }

    private static async Task UpdateGameState(OldMaidGameState state, RawOldMaidGameState? data,
        IDiscordRestGuildAPI guildApi)
    {
        if (data == null) return;

        state.BaseBet = data.BaseBet;
        state.DumpedCards = data.DumpedCards.ToImmutableArray();
        state.TotalBet = data.TotalBet;
        state.CurrentPlayerOrder = data.CurrentPlayerOrder;
        state.LastActiveTime = DateTime.Parse(data.LastActiveTime);
        state.PreviousPlayerId = data.PreviousPlayerId;

        foreach (var (playerId, playerState) in data.Players)
        {
            if (state.Players.ContainsKey(playerId))
            {
                var oldPlayerState = state.Players.GetOrAdd(playerId, new OldMaidPlayerState());

                oldPlayerState.Cards = playerState.Cards.ToImmutableArray();
                oldPlayerState.Order = playerState.Order;
                oldPlayerState.AvatarUrl = playerState.AvatarUrl;
                oldPlayerState.PlayerName = playerState.PlayerName;

                if (oldPlayerState.TextChannel != null || playerState.ChannelId == 0) continue;

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
                var newPlayerState = new OldMaidPlayerState
                {
                    AvatarUrl = playerState.AvatarUrl,
                    Cards = playerState.Cards.ToImmutableArray(),
                    GameId = playerState.GameId,
                    Order = playerState.Order,
                    PlayerId = playerState.PlayerId,
                    PlayerName = playerState.PlayerName,
                    TextChannel = null
                };

                state.Players.AddOrUpdate(playerId, newPlayerState, (_, _) => newPlayerState);
            }
        }
    }
}