using System.ComponentModel;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Results;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands;

public class Start : CommandGroup
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    private readonly ILogger<Start> _logger;

    public Start(InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<Start> logger)
    {
        _context = context;
        _userApi = userApi;
        _interactionApi = interactionApi;
        _state = state;
        _logger = logger;
    }

    [Command("start")]
    [Description("Opt-in and create an account to start joining games!")]
    public async Task<IResult> Handle()
    {
        var member = await Utilities.GetUserMember(_context, _interactionApi,
            "You can only join the games in a guild!", _logger);
        
        if (member == null) return Result.FromSuccess();
        
        var botUser = await Utilities.GetBotUser(_userApi, _logger);
        
        if (botUser == null) return Result.FromSuccess();

        var newPlayer = new NewPlayer
        {
            PlayerId = _context.User.ID.Value.ToString(),
            Name = member.GetDisplayName(),
            AvatarUrl = _context.User.GetAvatarUrl()
        };

        var playerData = await _state.BurstApi.SendRawRequest("/player", ApiRequestType.Post, newPlayer);
        if (!playerData.ResponseMessage.IsSuccessStatusCode)
        {
            var errorResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    "Sorry! But you seem to have already joined!");

            return Result.FromError(errorResult);
        }

        var embed = new Embed(
            Author: new EmbedAuthor(member.GetDisplayName(), IconUrl: _context.User.GetAvatarUrl()),
            Colour: BurstColor.Burst.ToColor(),
            Description:
            "Congratulations! You can now start joining games to play poker with other people!\nAs a first time bonus, **you also have received 100 tips!**\nEnjoy!",
            Thumbnail: new EmbedThumbnail(botUser.GetAvatarUrl()),
            Title: "Welcome to the Jack of All Trades!",
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

    public override string ToString() => "start";
}