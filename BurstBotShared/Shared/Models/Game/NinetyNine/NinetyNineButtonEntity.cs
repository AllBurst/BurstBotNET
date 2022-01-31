using System.Text.Json;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;
using Microsoft.Extensions.Logging;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineButtonEntity : IButtonInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<NinetyNineButtonEntity> _logger;

    public NinetyNineButtonEntity(
        InteractionContext context,
        IDiscordRestChannelAPI channelAPI,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<NinetyNineButtonEntity> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _channelApi = channelAPI;
        _logger = logger;
    }
    private readonly string[] _validCustomIds =
{
        "plus10",
        "plus20",
        "minus10",
        "minus20",
        "confirm",
        "ninety_nine_help_selection"
    };

    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(_validCustomIds.Contains(customId));
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        var gameState = Utilities.Utilities
            .GetGameState<NinetyNineGameState, NinetyNinePlayerState, NinetyNineGameProgress>(user,
                _state.GameStates.NinetyNineGameStates.Item1,
                _state.GameStates.NinetyNineGameStates.Item2,
                _context,
                out var playerState);

        if (playerState?.TextChannel == null) return Result.FromSuccess();

        var sanitizedCustomId = customId.Trim();
        return sanitizedCustomId switch
        {
            "plus10" or "plus20" => await PlusOrMinus(message!, gameState, playerState, NinetyNineInGameAdjustmentType.Plus, ct),
            "minus10" or "minus20" => await PlusOrMinus(message!,gameState, playerState,NinetyNineInGameAdjustmentType.Minus, ct),
            "confirm" => await GiveUp(message!,gameState,playerState,ct),
            "ninety_nine_help_selection" => await ShowHelpMenu(),
            _ => Result.FromSuccess()
        };
    }

    private async Task<Result> PlusOrMinus(
    IMessage message,
    NinetyNineGameState gameState,
    NinetyNinePlayerState playerState,
    NinetyNineInGameAdjustmentType plusOrMinus,
    CancellationToken ct)
    {
        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayCard = playerState.UniversalCard,
                PlayerId = playerState.PlayerId,
                Adjustment = plusOrMinus,
                RequestType = NinetyNineInGameRequestType.Play
            })), ct);
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }
    private async Task<Result> GiveUp(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        CancellationToken ct)
    {
        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                RequestType = NinetyNineInGameRequestType.Burst
            })), ct);
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);

        return Result.FromSuccess();
    }

    private async Task<Result> ShowHelpMenu()
    {
        var localization = _state.Localizations.GetLocalization().NinetyNine;

        var result = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                localization.CommandList["draw"]);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}
