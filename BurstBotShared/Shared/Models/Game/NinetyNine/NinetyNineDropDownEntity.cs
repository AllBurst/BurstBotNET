using System.Text.Json;
using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
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
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<NinetyNineDropDownEntity> _logger;

    private readonly string[] _validCustomIds =
    {
        "ninety_nine_user_selection",
        "ninety_nine_five_selection"
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
            "ninety_nine_five_selection" => await PlayFive(message!, gameState, playerState, values, ct),
            _ => Result.FromSuccess()
        };
    }

    private async Task<Result> Play(
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
                playerState.UniversalCard = extractedCard;

                var playerNameList = gameState.Players
                    .Where(p => p.Value.PlayerId != playerState.PlayerId && !gameState.BurstPlayers.Contains(p.Value.PlayerId))
                    .Select(p => new SelectOption(p.Value.PlayerName, p.Key.ToString()));

                var fiveSelectMenu = new SelectMenuComponent("ninety_nine_five_selection",
                    playerNameList.ToImmutableArray(),
                    localization.SelectPlayerMessage, 1, 1);

                var component = (new IMessageComponent[]
                {
                    new ActionRowComponent(new[]
                    {
                        fiveSelectMenu
                    })
                });

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        localization.SelectPlayerMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
            }
            case 10 or 12:
            {
                playerState.UniversalCard = extractedCard;

                var count = extractedCard.Number == 12 ? extractedCard.Number + 8 : extractedCard.Number;

                var plusButton = new ButtonComponent(ButtonComponentStyle.Success, localization.Plus,
                    new PartialEmoji(Name: "➕"), $"plus{count}");
                var minusButton = new ButtonComponent(ButtonComponentStyle.Danger, localization.Minus,
                    new PartialEmoji(Name: "➖"), $"minus{count}");

                var buttonComponents = new List<IMessageComponent>();

                if (gameState.CurrentTotal + count <= 99)
                    buttonComponents.Add(plusButton);
                if (gameState.CurrentTotal - count >= 0)
                    buttonComponents.Add(minusButton);


                var component = new IMessageComponent[]
                {
                    new ActionRowComponent(buttonComponents)
                };

                var sendResult = await _channelApi
                    .CreateMessageAsync(message.ChannelID,
                        content: localization.PlusOrMinusMessage,
                        components: component,
                        ct: ct);

                return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
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
                PlayCard = extractedCard,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

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
                PlayCard = playerState.UniversalCard,
                PlayerId = playerState.PlayerId,
                SpecifiedPlayer = nextPlayerId,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);

        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
}