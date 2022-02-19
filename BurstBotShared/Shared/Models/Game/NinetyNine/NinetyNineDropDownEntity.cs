using System.Text.Json;
using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.NinetyNine.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;
using Remora.Discord.API.Objects;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineDropDownEntity : ISelectMenuInteractiveEntity
{
    private const string PlayerSelectionCustomId = "ninety_nine_player_selection";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<NinetyNineDropDownEntity> _logger;

    private readonly string[] _validCustomIds =
    {
        "ninety_nine_user_selection",
        PlayerSelectionCustomId
    };

    public NinetyNineDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<NinetyNineDropDownEntity> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }

    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId,
        CancellationToken ct = new())
    {
        return componentType is not ComponentType.SelectMenu
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(_validCustomIds.Contains(customId));
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        var gameState = Utilities.Utilities
            .GetGameState<NinetyNineGameState, NinetyNinePlayerState, NinetyNineGameProgress>(user,
                _state.GameStates.NinetyNineGameStates.Item1,
                _state.GameStates.NinetyNineGameStates.Item2,
                _context,
                out var playerState);

        if (playerState == null) return Result.FromSuccess();

        var sanitizedCustomId = customId.Trim();

        return sanitizedCustomId switch
        {
            "ninety_nine_user_selection" => await Play(message!, gameState, playerState, values, ct),
            var id when string.Equals(id, PlayerSelectionCustomId) => await PlayFive(message!, gameState, playerState,
                values, ct),
            _ => Result.FromSuccess()
        };
    }
    
    private static IMessageComponent[] BuildPlayerSelectMenu(Card card, NinetyNineGameState gameState, NinetyNinePlayerState playerState, NinetyNineLocalization localization)
    {
        var playerNameList = gameState.Players
            .Where(p => p.Value.PlayerId != playerState.PlayerId && !gameState.BurstPlayers.Contains(p.Value.PlayerId))
            .Where(p =>
            {
                var (_, pState) = p;
                return card.Number != 5 || pState.PassTimes == 0;
            })
            .Select(p => new SelectOption(p.Value.PlayerName, p.Key.ToString()));

        var selectMenu = new SelectMenuComponent(PlayerSelectionCustomId,
            playerNameList.ToImmutableArray(),
            localization.SelectPlayerMessage, 1, 1);

        return new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                selectMenu
            })
        };
    }
    
    private static IMessageComponent[] BuildPlusMinusButtons(int number, int currentTotal, NinetyNineLocalization localization)
    {
        var plusButton = new ButtonComponent(ButtonComponentStyle.Success, localization.Plus,
            new PartialEmoji(Name: "➕"), $"plus{number}");
        var minusButton = new ButtonComponent(ButtonComponentStyle.Danger, localization.Minus,
            new PartialEmoji(Name: "➖"), $"minus{number}");

        var buttonComponents = new List<IMessageComponent>();

        if (currentTotal + number <= 99)
            buttonComponents.Add(plusButton);
        if (currentTotal - number >= 0)
            buttonComponents.Add(minusButton);
                    
        return new IMessageComponent[]
        {
            new ActionRowComponent(buttonComponents)
        };
    }

    private async Task<Result> Play(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        return gameState.Variation switch
        {
            NinetyNineVariation.Taiwanese => await PlayTaiwanese(message, gameState, playerState, values, ct),
            NinetyNineVariation.Icelandic => await PlayIcelandic(message, gameState, playerState, values, ct),
            NinetyNineVariation.Standard => await PlayStandard(message, gameState, playerState, values, ct),
            _ => await PlayTaiwanese(message, gameState, playerState, values, ct)
        };
    }

    private async Task<Result> PlayStandard(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCard = Utilities.Utilities.ExtractCard(values);
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        switch (extractedCard.Number)
        {
            case 1:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);
                
                var plusOne = new ButtonComponent(ButtonComponentStyle.Success,
                    localization.PlusSpecific.Replace("{number}", 1.ToString(CultureInfo.InvariantCulture)),
                    new PartialEmoji(Name: "➕"), $"plus1");
                var plusEleven = new ButtonComponent(ButtonComponentStyle.Success,
                    localization.PlusSpecific.Replace("{number}", 11.ToString(CultureInfo.InvariantCulture)),
                    new PartialEmoji(Name: "➕"), $"plus11");
                    
                var buttonComponents = new List<IMessageComponent>();

                if (gameState.CurrentTotal + 1 <= 99)
                    buttonComponents.Add(plusOne);
                if (gameState.CurrentTotal + 11 <= 99)
                    buttonComponents.Add(plusEleven);
                    
                var component = new IMessageComponent[]
                {
                    new ActionRowComponent(buttonComponents)
                };
                    
                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOneOrEleven,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
            case 10:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);
                
                var component = BuildPlusMinusButtons(10, gameState.CurrentTotal, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOrMinusMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
        }
        
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayCards = new[] { extractedCard },
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        return Result.FromSuccess();
    }

    private async Task<Result> PlayIcelandic(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCards = values
            .Select(Utilities.Utilities.ExtractCard)
            .ToImmutableArray();
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        if (extractedCards.Length > 1 && extractedCards.Any(c => c.Number == 12))
        {
            var sendResult = await _channelApi
                .CreateMessageAsync(
                    playerState.TextChannel!.ID,
                    localization.NotOnlyQueen,
                    ct: ct);
            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        foreach (var card in extractedCards)
        {
            playerState.TemporaryCards.Enqueue(card);
            
            switch (card.Number)
            {
                case 1:
                {
                    playerState.ResponsesInWait++;

                    var plusOne = new ButtonComponent(ButtonComponentStyle.Success,
                        localization.PlusSpecific.Replace("{number}", 1.ToString(CultureInfo.InvariantCulture)),
                        new PartialEmoji(Name: "➕"), $"plus1");
                    var plusFourteen = new ButtonComponent(ButtonComponentStyle.Success,
                        localization.PlusSpecific.Replace("{number}", 14.ToString(CultureInfo.InvariantCulture)),
                        new PartialEmoji(Name: "➕"), $"plus14");
                    
                    var buttonComponents = new List<IMessageComponent>();

                    if (gameState.CurrentTotal + 1 <= 99)
                        buttonComponents.Add(plusOne);
                    if (gameState.CurrentTotal + 14 <= 99)
                        buttonComponents.Add(plusFourteen);
                    
                    var component = new IMessageComponent[]
                    {
                        new ActionRowComponent(buttonComponents)
                    };
                    
                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.PlusOneOrFourteen,
                            components: component,
                            ct: ct);

                    if (sendResult.IsSuccess) continue;

                    _logger.LogError("Failed to send buttons for Ace: {Reason}, inner: {Inner}",
                        sendResult.Error.Message, sendResult.Inner);

                    break;
                }
                case 10:
                {
                    playerState.ResponsesInWait++;
                    
                    var component = BuildPlusMinusButtons(10, gameState.CurrentTotal, localization);

                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.PlusOrMinusMessage,
                            components: component,
                            ct: ct);

                    if (sendResult.IsSuccess) continue;

                    _logger.LogError("Failed to send select menu for Ten: {Reason}, inner: {Inner}",
                        sendResult.Error.Message, sendResult.Inner);

                    break;
                }
            }
        }

        if (playerState.ResponsesInWait > 0) return Result.FromSuccess();
        
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayCards = playerState.TemporaryCards,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        playerState.TemporaryCards.Clear();

        return Result.FromSuccess();
    }

    private async Task<Result> PlayTaiwanese(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var extractedCard = Utilities.Utilities.ExtractCard(values);
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        switch (extractedCard.Number)
        {
            case 5:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);

                var component = BuildPlayerSelectMenu(extractedCard, gameState, playerState, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.SelectPlayerMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
            case 10 or 12:
            {
                playerState.TemporaryCards.Enqueue(extractedCard);

                var count = extractedCard.Number == 12 ? 20 : 10;

                var component = BuildPlusMinusButtons(count, gameState.CurrentTotal, localization);

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.PlusOrMinusMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
        }

        if (gameState.Variation == NinetyNineVariation.Bloody)
        {
            switch (extractedCard.Number)
            {
                case 1:
                {
                    if (extractedCard.Suit == Suit.Spade) break;
                    
                    playerState.TemporaryCards.Enqueue(extractedCard);
                    var component = BuildPlayerSelectMenu(extractedCard, gameState, playerState, localization);

                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.SelectPlayerMessage,
                            components: component,
                            ct: ct);

                    return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
                }
                case 7:
                {
                    playerState.TemporaryCards.Enqueue(extractedCard);
                    
                    var playerNameList = gameState.Players
                        .Where(p =>
                        {
                            var (pId, pState) = p;
                            var notSelf = pId != playerState.PlayerId;
                            var notBurst = !gameState.BurstPlayers.Contains(pId);
                            var hasCards = !pState.Cards.IsEmpty;
                            var notDrawn = !pState.IsDrawn;
                            
                            return notSelf && notBurst && hasCards && notDrawn;
                        })
                        .Select(p => new SelectOption(p.Value.PlayerName, p.Key.ToString()));

                    var selectMenu = new SelectMenuComponent(PlayerSelectionCustomId,
                        playerNameList.ToImmutableArray(),
                        localization.SelectPlayerMessage, 1, 1);

                    var component = new IMessageComponent[]
                    {
                        new ActionRowComponent(new[]
                        {
                            selectMenu
                        })
                    };
                    
                    var sendResult = await _channelApi
                        .CreateMessageAsync(message.ChannelID,
                            localization.SelectPlayerMessage,
                            components: component,
                            ct: ct);

                    return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
                }
            }
        }

        var currentPlayer = gameState
            .Players
            .First(p => p.Value.Order == gameState.CurrentPlayerOrder)
            .Value;

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            currentPlayer.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = currentPlayer.PlayerId,
                PlayCards = new[] { extractedCard },
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        return Result.FromSuccess();
    }

    private async Task<Result> PlayFive(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var nextPlayerId = ulong.Parse(values.First());

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            nextPlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayCards = playerState.TemporaryCards,
                PlayerId = playerState.PlayerId,
                SpecifiedPlayer = nextPlayerId,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        playerState.TemporaryCards.Clear();
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
}