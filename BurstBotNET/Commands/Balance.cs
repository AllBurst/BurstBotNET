using System.Collections.Immutable;
using System.ComponentModel;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands;

public class Balance : CommandGroup, ISlashCommand
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly ILogger<Balance> _logger;
    private readonly State _state;

    public Balance(
        InteractionContext context,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestUserAPI userApi,
        State state,
        ILogger<Balance> logger)
    {
        _context = context;
        _interactionApi = interactionApi;
        _userApi = userApi;
        _logger = logger;
        _state = state;
    }

    public static string Name => "balance";

    public static string Description => "Check how many tips you currently have.";

    public static ImmutableArray<IApplicationCommandOption> ApplicationCommandOptions => ImmutableArray<IApplicationCommandOption>.Empty;

    public static Tuple<string, string, ImmutableArray<IApplicationCommandOption>> GetCommandTuple()
    {
        return new Tuple<string, string, ImmutableArray<IApplicationCommandOption>>(Name, Description, ApplicationCommandOptions);
    }

    [Command("balance")]
    [Description("Check how many tips you currently have.")]
    public async Task<IResult> Handle()
    {
        var member = await Utilities.GetUserMember(_context, _interactionApi,
            "Sorry, but you can only check your balance in a guild!", _logger);
        
        if (member == null) return Result.FromSuccess();

        var playerData = await _state.BurstApi.SendRawRequest<object>(
            $"/player/{member.User.Value.ID.Value}", ApiRequestType.Get, null);
        if (!playerData.ResponseMessage.IsSuccessStatusCode)
        {
            var errorMessage = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    "Sorry! But have you already opted in?\nIf not, please use `/start` command so we can enroll you!");
            return errorMessage.IsSuccess ? Result.FromSuccess() : Result.FromError(errorMessage);
        }

        var bot = await Utilities.GetBotUser(_userApi, _logger);

        if (bot == null) return Result.FromSuccess();

        var displayName = member.GetDisplayName();
        var embed = new Embed(
            Author: new EmbedAuthor(displayName, IconUrl: member.User.Value.GetAvatarUrl()),
            Colour: BurstColor.Burst.ToColor(),
            Description: "Here is your account balance.",
            Thumbnail: new EmbedThumbnail(bot.GetAvatarUrl()),
            Title: "Account Balance",
            Fields: new[]
            {
                new EmbedField("Current Tips", (await playerData.GetJsonAsync<RawPlayer>()).Amount.ToString())
            });

        var result = await _interactionApi
            .EditOriginalInteractionResponseAsync(
                _context.ApplicationID,
                _context.Token,
                embeds: new[] { embed });
        return result.IsSuccess ? Result.FromSuccess() : Result.FromError(result);
    }

    public override string ToString() => "balance";
}