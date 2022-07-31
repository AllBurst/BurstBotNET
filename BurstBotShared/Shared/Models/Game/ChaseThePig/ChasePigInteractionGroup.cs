using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChaseThePig.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using BurstBotShared.Shared.Models.Localization.ChaseThePig;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Interactivity;
using Remora.Results;

namespace BurstBotShared.Shared.Models.Game.ChaseThePig;

public class ChasePigInteractionGroup : InteractionGroup, IHelpButtonEntity
{
    private static readonly TextInfo TextInfo = CultureInfo.InvariantCulture.TextInfo;
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<ChasePigInteractionGroup> _logger;
    private readonly IDiscordRestChannelAPI _channelApi;
    
    private readonly string[] _validCustomIds =
    {
        "chase_pig_expose_menu",
        "chase_pig_card_selection",
        "chase_pig_help_selection"
    };

    public ChasePigInteractionGroup(
        InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestChannelAPI channelApi,
        ILogger<ChasePigInteractionGroup> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _channelApi = channelApi;
        _logger = logger;
    }

    [Button("chase_pig_decline_expose")]
    public async Task<IResult> DeclineExpose() =>
        await HandleInteractionAsync(_context.User, "chase_pig_decline_expose");

    [Button("chase_pig_help")]
    public async Task<IResult> Help() => await HandleInteractionAsync(_context.User, "chase_pig_help");

    [Button("chase_pig_confirm_no_exposable_cards")]
    public async Task<IResult> ConfirmNoExposable() =>
        await HandleInteractionAsync(_context.User, "chase_pig_confirm_no_exposable_cards");

    [SelectMenu("chase_pig_expose_menu")]
    public async Task<IResult> Expose(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, "chase_pig_expose_menu", values);
    
    [SelectMenu("chase_pig_card_selection")]
    public async Task<IResult> SelectCard(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, "chase_pig_card_selection", values);
    
    [SelectMenu("chase_pig_help_selection")]
    public async Task<IResult> SelectHelp(IReadOnlyList<string> values) =>
        await HandleInteractionAsync(_context.User, "chase_pig_help_selection", values);

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
                new SelectMenuComponent(CustomIDHelpers.CreateSelectMenuID("chase_pig_help_selection"), options.ToImmutableArray(), localization.ShowHelp, 1,
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
    
    private static async Task ExposeCard(
        ChasePigGameState gameState,
        ChasePigPlayerState playerState,
        IEnumerable<string> values)
    {
        var exposure = values
            .Select(Enum.Parse<ChasePigExposure>);

        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
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
        await gameState.RequestChannel!.Writer.WriteAsync(new Tuple<ulong, byte[]>(
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
    
    private async Task<Result> HandleInteractionAsync(IUser user, string customId, CancellationToken ct = new())
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
    
    private async Task<IResult> HandleInteractionAsync(IUser user, string customId, IReadOnlyList<string> values,
        CancellationToken ct = new())
    {
        var hasMessage = _context.Message.IsDefined(out var message);
        if (!hasMessage) return Result.FromSuccess();
        
        var sanitizedCustomId = customId.Trim();

        if (_state.GameStates.ChasePigGameStates.Item2.Contains(_context.ChannelID))
        {
            var gameState = Utilities.Utilities
                .GetGameState<ChasePigGameState, ChasePigPlayerState, ChasePigGameProgress>(user,
                    _state.GameStates.ChasePigGameStates.Item1,
                    _state.GameStates.ChasePigGameStates.Item2,
                    _context,
                    out var playerState);
            if (playerState == null) return Result.FromSuccess();

            switch (sanitizedCustomId)
            {
                case "chase_pig_expose_menu":
                    await ExposeCard(gameState, playerState, values);
                    await Utilities.Utilities.DisableComponents(message!, true, _channelApi, _logger, ct);
                    break;
                case "chase_pig_card_selection":
                    await PlayCard(gameState, playerState, values);
                    await Utilities.Utilities.DisableComponents(message!, true, _channelApi, _logger, ct);
                    break;
                case "chase_pig_help_selection":
                {
                    var result = await ShowHelpText(values, ct);
                    if (!result.IsSuccess)
                        _logger.LogError("Failed to show help text: {Reason}, inner: {Inner}", result.Error.Message,
                            result.Inner);
                    break;
                }
            }
        } else if (sanitizedCustomId == "chase_pig_help_selection")
        {
            return await ShowHelpText(values, ct);
        }

        return Result.FromSuccess();
    }

    private async Task<Result> ShowHelpText(
        IEnumerable<string> values,
        CancellationToken ct)
    {
        var localization = _state.Localizations.GetLocalization().ChaseThePig;
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