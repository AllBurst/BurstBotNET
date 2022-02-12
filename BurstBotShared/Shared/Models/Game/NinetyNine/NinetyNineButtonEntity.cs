using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;
using Microsoft.Extensions.Logging;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNineButtonEntity : IButtonInteractiveEntity, IHelpButtonEntity
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<NinetyNineButtonEntity> _logger;

    public NinetyNineButtonEntity(
        InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<NinetyNineButtonEntity> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _channelApi = channelApi;
        _logger = logger;
    }
    private readonly string[] _validCustomIds =
{
        "plus10",
        "plus20",
        "plus1",
        "plus14",
        "minus10",
        "minus20",
        "confirm",
        "ninety_nine_help_Taiwanese",
        "ninety_nine_help_Icelandic",
        "ninety_nine_help_Standard"
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
            "plus10" or "plus20" => await PlusOrMinus(message!, gameState, playerState,
                NinetyNineInGameAdjustmentType.Plus, ct),
            "minus10" or "minus20" => await PlusOrMinus(message!, gameState, playerState,
                NinetyNineInGameAdjustmentType.Minus, ct),
            "plus1" or "plus14" => await PlusOrMinus(message!, gameState, playerState,
                string.Equals(sanitizedCustomId, "plus1")
                    ? NinetyNineInGameAdjustmentType.One
                    : NinetyNineInGameAdjustmentType.Fourteen, ct),
            "ninety_nine_help_Taiwanese" => await ShowHelpMenu(NinetyNineVariation.Taiwanese, _context, _state,
                _interactionApi),
            "ninety_nine_help_Icelandic" => await ShowHelpMenu(NinetyNineVariation.Icelandic, _context, _state,
                _interactionApi),
            "ninety_nine_help_Standard" => await ShowHelpMenu(NinetyNineVariation.Standard, _context, _state,
                _interactionApi),
            "confirm" => await GiveUp(message!, gameState, playerState, ct),
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
        await Utilities.Utilities.DisableComponents(message, true, _channelApi, _logger, ct);
        
        switch (gameState.Variation)
        {
            case NinetyNineVariation.Taiwanese:
            {
                await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                    playerState.PlayerId,
                    JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
                    {
                        GameId = gameState.GameId,
                        PlayCards = playerState.TemporaryCards,
                        PlayerId = playerState.PlayerId,
                        Adjustments = new[] { plusOrMinus },
                        RequestType = NinetyNineInGameRequestType.Play
                    })), ct);

                break;
            }
            case NinetyNineVariation.Icelandic:
            {
                if (playerState.ResponsesInWait > 0)
                {
                    playerState.ResponsesInWait--;
                    playerState.TemporaryAdjustments.Enqueue(plusOrMinus);
                    break;
                }
                
                await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
                    playerState.PlayerId,
                    JsonSerializer.SerializeToUtf8Bytes(new NinetyNineInGameRequest
                    {
                        GameId = gameState.GameId,
                        PlayCards = playerState.TemporaryCards,
                        PlayerId = playerState.PlayerId,
                        Adjustments = playerState.TemporaryAdjustments,
                        RequestType = NinetyNineInGameRequestType.Play
                    })), ct);
                
                playerState.TemporaryCards.Clear();
                playerState.TemporaryAdjustments.Clear();
                playerState.ResponsesInWait = 0;
                
                break;
            }
        }
        
        return Result.FromSuccess();
    }
    private async Task<Result> GiveUp(
        IMessage message,
        NinetyNineGameState gameState,
        NinetyNinePlayerState playerState,
        CancellationToken ct)
    {
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
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

    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state,
        IDiscordRestInteractionAPI interactionApi)
        => await ShowHelpMenu(NinetyNineVariation.Taiwanese, context, state, interactionApi);

    public static async Task<Result> ShowHelpMenu(NinetyNineVariation variation, InteractionContext context,
        State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().NinetyNine;

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                variation switch
                {
                    NinetyNineVariation.Taiwanese => localization.CommandList["generalTaiwanese"],
                    NinetyNineVariation.Icelandic => localization.CommandList["generalIcelandic"],
                    NinetyNineVariation.Standard => localization.CommandList["generalStandard"],
                    _ => localization.CommandList["generalTaiwanese"]
                });

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}
