using System.ComponentModel;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Utilities;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Extensions.Embeds;
using Remora.Results;

namespace BurstBotNET.Commands;

public class Trade : CommandGroup
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<Trade> _logger;
    private readonly IDiscordRestGuildAPI _guildApi;

    public Trade(InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        ILogger<Trade> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _guildApi = guildApi;
        _logger = logger;
    }

    [Command("donate")]
    [CommandType(ApplicationCommandType.User)]
    [Description("Donate 500 tips to the player.")]
    public async Task<IResult> Donate()
    {
        var resolveResult = _context.Data.Resolved.IsDefined(out var resolved);
        if (!resolveResult)
            return Result.FromSuccess();

        resolveResult = resolved!.Users.IsDefined(out var users);
        if (!resolveResult)
            return Result.FromSuccess();

        resolveResult = _context.GuildID.IsDefined(out var guild);
        if (!resolveResult)
            return Result.FromSuccess();

        var donee = users!.First().Value;
        var donor = _context.User;
        var donorMember = await Utilities.GetUserMember(_context,
            _interactionApi,
            "Sorry, but you can only donate in a guild!",
            _logger);
        var doneeMember = await _guildApi
            .GetGuildMemberAsync(guild, donee.ID);

        if (donorMember == null || !doneeMember.IsSuccess) return Result.FromSuccess();
        
        var (isValid, invokerTip) = await Game.ValidatePlayers(
            donor.ID.Value,
            new[] { donor.ID.Value, donee.ID.Value },
            _state.BurstApi,
            _context,
            float.MinValue,
            _interactionApi);

        if (!isValid) return Result.FromSuccess();

        if (invokerTip!.Amount < 500)
        {
            var sendResult = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID,
                    _context.Token,
                    "Sorry, but you don't have enough tips to donate!");

            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }

        var response = await _state.BurstApi.SendRawRequest($"/tip/{invokerTip.PlayerId}",
            ApiRequestType.Patch, new UpdateTip
            {
                Adjustment = TipAdjustment.Minus,
                NewAmount = 500
            });
        if (!response.ResponseMessage.IsSuccessStatusCode)
        {
            var sendResult = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID,
                    _context.Token,
                    $"An error occurred when donating the player {donee.ID.Value}: {response.ResponseMessage.StatusCode} - {response.ResponseMessage.ReasonPhrase}");
            
            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }

        response = await _state.BurstApi.SendRawRequest($"/tip/{donee.ID.Value}",
            ApiRequestType.Patch, new UpdateTip
            {
                Adjustment = TipAdjustment.Plus,
                NewAmount = 500
            });
        if (!response.ResponseMessage.IsSuccessStatusCode)
        {
            var sendResult = await _interactionApi
                .CreateFollowupMessageAsync(_context.ApplicationID,
                    _context.Token,
                    $"An error occurred when donating the player {donee.ID.Value}: {response.ResponseMessage.StatusCode} - {response.ResponseMessage.ReasonPhrase}");
            
            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }

        var donorTip = await _state.BurstApi
            .SendRawRequest<object>($"/tip/{invokerTip.PlayerId}",
                ApiRequestType.Get, null)
            .ReceiveJson<RawTip>();
        
        var doneeTip = await _state.BurstApi
            .SendRawRequest<object>($"/tip/{donee.ID.Value}",
                ApiRequestType.Get, null)
            .ReceiveJson<RawTip>();

        var embed = new EmbedBuilder()
            .WithAuthor(donorMember.GetDisplayName(), iconUrl: donorMember.GetAvatarUrl())
            .WithColour(BurstColor.Burst.ToColor())
            .WithThumbnailUrl(Constants.BurstLogo)
            .WithTitle($"You donated 500 tips to {doneeMember.Entity.GetDisplayName()}!");

        embed.AddField($"{donorMember.GetDisplayName()}'s Tip", donorTip.Amount.ToString(), true);
        embed.AddField($"{doneeMember.Entity.GetDisplayName()}'s Tip", doneeTip.Amount.ToString(), true);

        var result = await _interactionApi
            .CreateFollowupMessageAsync(_context.ApplicationID, _context.Token,
                embeds: new[] { embed.Build().Entity });
        return result.IsSuccess ? Result.FromSuccess() : Result.FromError(result);
    }
}