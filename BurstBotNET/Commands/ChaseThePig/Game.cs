using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;

namespace BurstBotNET.Commands.ChaseThePig;

using ChasePigGame = IGame<ChasePigGameState, RawChasePigGameState, ChaseThePig, ChasePigPlayerState, ChasePigGameProgress, ChasePigInGameRequestType>;

public partial class ChaseThePig : ChasePigGame
{
    private static readonly Dictionary<ChasePigExposure, Card> ExposableCards = new()
    {
        { ChasePigExposure.Transformer, Card.CreateCard(Suit.Club, 10) },
        { ChasePigExposure.FirstTransformer, Card.CreateCard(Suit.Club, 10) },
        { ChasePigExposure.DoubleGoat, Card.CreateCard(Suit.Diamond, 11) },
        { ChasePigExposure.FirstDoubleGoat, Card.CreateCard(Suit.Diamond, 11) },
        { ChasePigExposure.DoublePig, Card.CreateCard(Suit.Spade, 12) },
        { ChasePigExposure.FirstDoublePig, Card.CreateCard(Suit.Spade, 12) },
        { ChasePigExposure.DoubleMinus, Card.CreateCard(Suit.Heart, 1) },
        { ChasePigExposure.FirstDoubleMinus, Card.CreateCard(Suit.Heart, 1) }
    };
    
    public static async Task<bool> HandleProgress(string messageContent, ChasePigGameState gameState, State state,
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        try
        {
            var deserializedIncomingData =
                JsonConvert.DeserializeObject<RawChasePigGameState>(messageContent, Game.JsonSerializerSettings);
            if (deserializedIncomingData == null) return false;
            
            await gameState.Semaphore.WaitAsync();
            logger.LogDebug("Semaphore acquired in HandleProgress");

            if (gameState.Progress != deserializedIncomingData.Progress)
            {
                logger.LogDebug("Progress changed, handling progress change...");
                var progressChangeResult = await HandleProgressChange(deserializedIncomingData, gameState, state,
                    channelApi, guildApi, logger);
                gameState.Semaphore.Release();
                logger.LogDebug("Semaphore released after progress change");
                return progressChangeResult;
            }

            await SendProgressMessages(gameState, deserializedIncomingData, state, channelApi, logger);
            
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception ex)
        {
            return Utilities.HandleException(ex, messageContent, gameState.Semaphore, logger);
        }

        return true;
    }

    public static Task<bool> HandleProgressChange(RawChasePigGameState deserializedIncomingData, ChasePigGameState gameState,
        State state, IDiscordRestChannelAPI channelApi, IDiscordRestGuildAPI guildApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    public static Task HandleEndingResult(string messageContent, ChasePigGameState state, Localizations localizations,
        DeckService deckService, IDiscordRestChannelAPI channelApi, ILogger logger)
    {
        throw new NotImplementedException();
    }

    private static async Task SendProgressMessages(ChasePigGameState gameState,
        RawChasePigGameState? deserializedIncomingData, State state, IDiscordRestChannelAPI channelApi, ILogger logger)
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
            case ChasePigGameProgress.Starting:
                await SendInitialMessage(previousPlayerOldState, previousPlayerNewState, deserializedIncomingData,
                    state.DeckService, state.Localizations, channelApi, logger);
                break;
            case ChasePigGameProgress.Exposing:
                await ShowPreviousPlayerAction(previousPlayerOldState!, previousPlayerNewState!,
                    gameState, deserializedIncomingData, state, channelApi, logger);
                await Task.Delay(TimeSpan.FromSeconds(1));
                await SendExposeMessage(gameState,
                    deserializedIncomingData, state.DeckService, state.Localizations,
                    channelApi, logger);
                break;
            case ChasePigGameProgress.Progressing:
            case ChasePigGameProgress.Ending:
                break;
        }
    }

    private static async Task SendInitialMessage(
        IPlayerState? playerState,
        RawChasePigPlayerState? newPlayerState,
        RawChasePigGameState deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger
    )
    {
        if (playerState?.TextChannel == null || newPlayerState == null) return;
        logger.LogDebug("Sending initial message...");

        var localization = localizations.GetLocalization().ChaseThePig;
        var prefix = localization.InitialMessagePrefix;

        var cardNames = prefix + string.Join('\n', newPlayerState.Cards);
        
        var description = localization.InitialMessageDescription
            .Replace("{baseBet}", deserializedIncomingData.BaseBet.ToString(CultureInfo.InvariantCulture))
            .Replace("{helpText}", localization.CommandList["general"])
            .Replace("{cardNames}", cardNames);
        
        await using var renderedDeck = SkiaService.RenderDeck(deckService, newPlayerState.Cards);
        
        var userEmbed = new Embed(
            Author: new EmbedAuthor(newPlayerState.PlayerName, IconUrl: newPlayerState.AvatarUrl),
            Description: description,
            Title: localization.InitialMessageTitle,
            Colour: BurstColor.Burst.ToColor(),
            Footer: new EmbedFooter(localization.InitialMessageFooter),
            Image: new EmbedImage(Constants.AttachmentUri),
            Thumbnail: new EmbedThumbnail(Constants.BurstLogo));
        
        var attachments = new[]
        {
            OneOf<FileData, IPartialAttachment>.FromT0(new FileData(Constants.OutputFileName, renderedDeck))
        };

        var sendResult = await channelApi
            .CreateMessageAsync(playerState.TextChannel.ID,
                embeds: new[] { userEmbed },
                attachments: attachments);
        if (!sendResult.IsSuccess)
            logger.LogError("Failed to send initial message to player {PlayerId}: {Reason}, inner: {Inner}",
                newPlayerState.PlayerId,
                sendResult.Error.Message, sendResult.Inner);
    }

    private static async Task SendExposeMessage(
        ChasePigGameState oldGameState,
        RawChasePigGameState? deserializedIncomingData,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (deserializedIncomingData == null) return;

        var localization = localizations.GetLocalization();

        var nextPlayer = deserializedIncomingData
            .Players
            .FirstOrDefault(p => p.Value.Order == deserializedIncomingData.CurrentPlayerOrder)
            .Value;

        var exposableCard = nextPlayer
            .Cards
            .Where(c => ExposableCards.ContainsValue(c))
            .ToImmutableArray();

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;
                
            var isNextPlayer = player.PlayerId == nextPlayer.PlayerId;
            
            var title = localization
                .ChaseThePig
                .TurnMessageTitle
                .Replace("{possessive}",
                    isNextPlayer
                        ? localization.GenericWords.PossessiveSecond.ToLowerInvariant()
                        : nextPlayer.PlayerName);

            if (exposableCard.IsEmpty)
            {
                var description = isNextPlayer
                    ? localization.ChaseThePig.NoExposableCardsSecond
                    : localization.ChaseThePig.NoExposableCardsThird.Replace("{playerName}", nextPlayer.PlayerName);

                var embed = new Embed
                {
                    Author = new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
                    Colour = BurstColor.Burst.ToColor(),
                    Description = description,
                    Title = title,
                    Thumbnail = new EmbedThumbnail(Constants.BurstLogo)
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed });
                if (sendResult.IsSuccess) continue;

                logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, sendResult.Error.Message, sendResult.Inner);
            }
            else
            {
                var embed = new Embed
                {
                    Author = new EmbedAuthor(nextPlayer.PlayerName, IconUrl: nextPlayer.AvatarUrl),
                    Colour = BurstColor.Burst.ToColor(),
                    Title = title,
                    Thumbnail = new EmbedThumbnail(Constants.BurstLogo)
                };

                if (isNextPlayer)
                {
                    await using var renderedCards = SkiaService.RenderDeck(deckService, exposableCard);
                    
                    embed = embed with
                    {
                        Description = localization.ChaseThePig.Expose,
                        Image = new EmbedImage(Constants.AttachmentUri)
                    };
                    
                    var attachments = new OneOf<FileData, IPartialAttachment>[]
                    {
                        new FileData(Constants.OutputFileName, renderedCards)
                    };

                    var selectOptions = exposableCard
                        .Select(c => new SelectOption(c.ToStringSimple(), c.ToSpecifier(), c.ToStringSimple(),
                            new PartialEmoji(c.Suit.ToSnowflake())));

                    var components = new IMessageComponent[]
                    {
                        new ActionRowComponent(new[]
                        {
                            new SelectMenuComponent("chase_pig_expose_menu", selectOptions.ToImmutableArray(),
                                localization.ChaseThePig.Expose, 0, 4)
                        })
                    };

                    var sendResult = await channelApi
                        .CreateMessageAsync(player.TextChannel.ID,
                            embeds: new[] { embed },
                            attachments: attachments,
                            components: components);
                    if (sendResult.IsSuccess) continue;

                    logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                        player.PlayerId, sendResult.Error.Message, sendResult.Inner);

                    continue;
                }

                var result = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed });
                if (result.IsSuccess) continue;

                logger.LogError("Failed to send expose message to {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, result.Error.Message, result.Inner);
            }
        }
    }

    private static async Task SendPreviousPlayerExposeMessage(
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        DeckService deckService,
        Localizations localizations,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = localizations.GetLocalization().ChaseThePig;

        var newExposures = newGameState
            .Exposures
            .Except(oldGameState.Exposures)
            .ToImmutableArray();

        var hasNewExposure = !newExposures.IsEmpty;

        if (!hasNewExposure)
        {
            var embed = new Embed(
                Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo));

            foreach (var (_, player) in oldGameState.Players)
            {
                if (player.TextChannel == null) continue;

                embed = embed with
                {
                    Title = localization.NoExposeMessage.Replace("{playerName}",
                        player.PlayerId == previousPlayerNewState.PlayerId
                            ? localizations.GetLocalization().GenericWords.Pronoun
                            : previousPlayerNewState.PlayerName)
                };

                var sendResult = await channelApi
                    .CreateMessageAsync(player.TextChannel.ID,
                        embeds: new[] { embed });
                
                if (sendResult.IsSuccess) continue;

                logger.LogError("Failed to send previous player's expose message to {PlayerId}: {Reason}, inner: {Inner}",
                    player.PlayerId, sendResult.Error.Message, sendResult.Inner);
            }

            return;
        }
        
        var playerFirstCard = previousPlayerNewState.Cards.FirstOrDefault();
        if (playerFirstCard == null) return;

        var exposure = newExposures.Last();

        var exposedCard = ExposableCards[exposure];

        var title = (exposure switch
            {
                ChasePigExposure.Transformer or ChasePigExposure.DoubleGoat or ChasePigExposure.DoubleMinus
                    or ChasePigExposure.DoublePig => localization.ExposeMessage,
                ChasePigExposure.FirstTransformer or ChasePigExposure.FirstDoubleGoat
                    or ChasePigExposure.FirstDoubleMinus
                    or ChasePigExposure.FirstDoublePig => localization.ExposeMessageFirst,
                _ => string.Empty
            }).Replace("{card}", exposedCard.ToStringSimple());

        var description = exposure switch
        {
            ChasePigExposure.Transformer => localization.ExposeTransformer.Replace("{ratio}", 2.ToString()),
            ChasePigExposure.DoubleGoat => localization.ExposeGoat.Replace("{ratio}", 2.ToString()),
            ChasePigExposure.DoubleMinus => localization.ExposeHeartA.Replace("{ratio}", 2.ToString()),
            ChasePigExposure.DoublePig => localization.ExposePig.Replace("{ratio}", 2.ToString()),
            ChasePigExposure.FirstTransformer => localization.ExposeTransformer.Replace("{ratio}", 4.ToString()),
            ChasePigExposure.FirstDoubleGoat => localization.ExposeGoat.Replace("{ratio}", 4.ToString()),
            ChasePigExposure.FirstDoubleMinus => localization.ExposeHeartA.Replace("{ratio}", 4.ToString()),
            ChasePigExposure.FirstDoublePig => localization.ExposePig.Replace("{ratio}", 4.ToString()),
            _ => ""
        };
        
        await using var renderedCard = SkiaService.RenderCard(deckService, exposedCard);

        foreach (var (_, player) in oldGameState.Players)
        {
            if (player.TextChannel == null) continue;

            var randomFileName = Utilities.GenerateRandomString();
            var randomAttachmentUri = $"attachment://{randomFileName}.jpg";

            var embed = new Embed(
                Author: new EmbedAuthor(previousPlayerNewState.PlayerName, IconUrl: previousPlayerNewState.AvatarUrl),
                Title: title.Replace("{playerName}", player.PlayerId == previousPlayerNewState.PlayerId
                    ? localizations.GetLocalization().GenericWords.Pronoun
                    : previousPlayerNewState.PlayerName),
                Description: description,
                Colour: BurstColor.Burst.ToColor(),
                Thumbnail: new EmbedThumbnail(Constants.BurstLogo),
                Image: new EmbedImage(randomAttachmentUri));

            await using var imageCopy = new MemoryStream((int)renderedCard.Length);
            await renderedCard.CopyToAsync(imageCopy);
            renderedCard.Seek(0, SeekOrigin.Begin);
            imageCopy.Seek(0, SeekOrigin.Begin);

            var attachments = new OneOf<FileData, IPartialAttachment>[]
            {
                new FileData(randomFileName, imageCopy)
            };

            var sendResult = await channelApi
                .CreateMessageAsync(player.TextChannel.ID,
                    embeds: new[] { embed },
                    attachments: attachments);
            if (sendResult.IsSuccess) continue;
            
            logger.LogError("Failed to send previous player's expose message to {PlayerId}: {Reason}, inner: {Inner}",
                player.PlayerId, sendResult.Error.Message, sendResult.Inner);
        }
    }

    private static async Task ShowPreviousPlayerAction(
        ChasePigPlayerState previousPlayerOldState,
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        if (oldGameState.Progress == ChasePigGameProgress.Starting) return;

        var localization = state.Localizations.GetLocalization().ChaseThePig;

        switch (oldGameState.Progress)
        {
            case ChasePigGameProgress.Exposing:
                await SendPreviousPlayerExposeMessage(previousPlayerNewState, oldGameState,
                    newGameState, state.DeckService, state.Localizations, channelApi, logger);
                break;
            case ChasePigGameProgress.Progressing:
                break;
        }
    }

    private static async Task SendPreviousPlayerActionMessage(
        ChasePigPlayerState previousPlayerOldState,
        RawChasePigPlayerState previousPlayerNewState,
        ChasePigGameState oldGameState,
        RawChasePigGameState newGameState,
        State state,
        IDiscordRestChannelAPI channelApi,
        ILogger logger)
    {
        var localization = state.Localizations.GetLocalization();

        var diff = previousPlayerOldState
            .Cards
            .Except(previousPlayerNewState.Cards)
            .ToImmutableArray();

        if (diff.IsEmpty) return;

        var playedCard = diff.Last();
        oldGameState.CardsOnTable.Add(playedCard);

        var title = localization
            .ChaseThePig
            .PlayMessage
            .Replace("{card}", playedCard.ToString());
        
        
    }
}