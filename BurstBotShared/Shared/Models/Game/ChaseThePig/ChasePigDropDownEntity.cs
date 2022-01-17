using System.Text.Json;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig;

public class ChasePigDropDownEntity : ISelectMenuInteractiveEntity
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<ChasePigDropDownEntity> _logger;

    private readonly string[] _validCustomIds =
    {
        "chase_pig_expose_menu",
        "chase_pig_card_selection",
        "chase_pig_help_selection"
    };
    
    public ChasePigDropDownEntity(InteractionContext context,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<ChasePigDropDownEntity> logger)
    {
        _context = context;
        _channelApi = channelApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }
    
    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
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
            .GetGameState<ChasePigGameState, ChasePigPlayerState, ChasePigGameProgress>(user,
                _state.GameStates.ChasePigGameStates.Item1,
                _state.GameStates.ChasePigGameStates.Item2,
                _context,
                out var playerState);
        if (playerState == null) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        switch (sanitizedCustomId)
        {
            case "chase_pig_expose_menu":
                await ExposeCard(gameState, playerState, values);
                await Utilities.Utilities.DisableComponents(message!, _channelApi, _logger, ct);
                break;
            case "chase_pig_card_selection":
                await PlayCard(gameState, playerState, values);
                await Utilities.Utilities.DisableComponents(message!, _channelApi, _logger, ct);
                break;
        }

        return Result.FromSuccess();
    }

    private static async Task ExposeCard(
        ChasePigGameState gameState,
        ChasePigPlayerState playerState,
        IEnumerable<string> values)
    {
        var exposure = values
            .Select(Enum.Parse<ChasePigExposure>);

        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChasePigInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                Exposures = exposure.ToList(),
                PlayerOrder = playerState.Order,
                RequestType = ChasePigInGameRequestType.Expose
            })));
    }

    private static async Task PlayCard(ChasePigGameState gameState, ChasePigPlayerState playerState,
        IEnumerable<string> values)
    {
        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) return;

        var pickedCard = Card.Create(value[..1], value[1..]);
        await gameState.Channel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChasePigInGameRequest
            {
                RequestType = ChasePigInGameRequestType.Play,
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayerOrder = playerState.Order,
                PlayCard = pickedCard
            })));
    }

    private async Task<Result> ShowHelpText(
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().RedDotsPicking;
        var content = localization.CommandList[values.FirstOrDefault()!];

        var texts = new List<string>();
        if (content.Length <= 2000)
            texts.Add(content);
        else
        {
            texts.Add(content[..2000]);
            texts.Add(content[2000..]);
        }

        foreach (var str in texts)
        {
            var result = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                    str, ct: ct);
            if (!result.IsSuccess)
                return Result.FromError(result);

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return Result.FromSuccess();
    }
}