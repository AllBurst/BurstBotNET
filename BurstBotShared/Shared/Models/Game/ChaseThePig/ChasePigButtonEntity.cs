using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Localization.ChaseThePig;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig;

public class ChasePigButtonEntity : IButtonInteractiveEntity, IHelpButtonEntity
{
    private static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<ChasePigButtonEntity> _logger;
    private readonly IDiscordRestChannelAPI _channelApi;

    public ChasePigButtonEntity(
        InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestChannelAPI channelApi,
        ILogger<ChasePigButtonEntity> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _channelApi = channelApi;
        _logger = logger;
    }

    public Task<Result<bool>> IsInterestedAsync(ComponentType componentType, string customId, CancellationToken ct = new())
    {
        var isValidButton = customId is "chase_pig_decline_expose" or "chase_pig_help" or "chase_pig_confirm_no_exposable_cards";
        return componentType is not ComponentType.Button
            ? Task.FromResult<Result<bool>>(false)
            : Task.FromResult<Result<bool>>(isValidButton);
    }

    public async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();

        var gameState = Utilities.Utilities
            .GetGameState<ChasePigGameState, ChasePigPlayerState, ChasePigGameProgress>(user,
                _state.GameStates.ChasePigGameStates.Item1,
                _state.GameStates.ChasePigGameStates.Item2,
                _context,
                out var playerState);
        
        if (playerState?.TextChannel == null) return Result.FromSuccess();

        var sanitizedCustomId = customId.Trim();

        switch (sanitizedCustomId)
        {
            case "chase_pig_help":
            {
                var result = await ShowHelpMenu(_context, _state, _interactionApi);
                if (!result.IsSuccess)
                    _logger.LogError("Failed to show help menu: {Reason}, inner: {Inner}",
                        result.Error.Message, result.Inner);
                break;
            }
            case "chase_pig_decline_expose":
            case "chase_pig_confirm_no_exposable_cards":
            {
                await SendNoExposureToChannel(gameState, playerState);
                await Utilities.Utilities.DisableComponents(message!, true, _channelApi, _logger, ct);
                break;
            }
        }

        return Result.FromSuccess();
    }
    
    public static async Task<Result> ShowHelpMenu(InteractionContext context, State state, IDiscordRestInteractionAPI interactionApi)
    {
        var localization = state.Localizations.GetLocalization().ChaseThePig;

        var options = localization
            .CommandList
            .Keys
            .Select(k =>
            {
                var (desc, emoji) = GetHelpMenuItemDescription(k, localization);
                return (k, desc, emoji);
            })
            .Select(item => new SelectOption(TextInfo.ToTitleCase(item.k), item.k, item.desc, item.emoji));

        var components = new IMessageComponent[]
        {
            new ActionRowComponent(new[]
            {
                new SelectMenuComponent("chase_pig_help_selection", options.ToImmutableArray(), localization.ShowHelp, 1,
                    1)
            })
        };

        var result = await interactionApi
            .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                localization.About,
                components: components);

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }

    private static (string, PartialEmoji) GetHelpMenuItemDescription(string key, ChasePigLocalization localization)
        => key switch
        {
            "general" => (localization.ShowGeneral, new PartialEmoji(Name: "ℹ️")),
            "scoring" => (localization.ShowScoring, new PartialEmoji(Name: "💯")),
            "flows" => (localization.ShowFlows, new PartialEmoji(Name: "➡️")),
            "exposure" => (localization.ShowExposure, new PartialEmoji(Name: "✨")),
            _ => ("", new PartialEmoji(Name: "❓"))
        };

    private static async Task SendNoExposureToChannel(ChasePigGameState gameState, ChasePigPlayerState playerState)
    {
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
            playerState.PlayerId,
            JsonSerializer.SerializeToUtf8Bytes(new ChasePigInGameRequest
            {
                GameId = gameState.GameId,
                PlayerId = playerState.PlayerId,
                PlayerOrder = playerState.Order,
                Exposures = new List<ChasePigExposure>(),
                RequestType = ChasePigInGameRequestType.Expose
            })));
    }
}