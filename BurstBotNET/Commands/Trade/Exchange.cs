using System.ComponentModel;
using System.Globalization;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Utilities;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.Extensions.Embeds;
using Remora.Results;

namespace BurstBotNET.Commands.Trade;

public partial class Trade
{
    [Command("exchange")]
    [Description("Exchange for lottery credits with your own tips. 10 tips = 1 credit.")]
    public async Task<IResult> Exchange(
        [Description("The amount of tips you want to exchange for lottery credits. 10 tips = 1 credit.")]
        int amount)
    {
        var member = await Utilities.GetUserMember(_context, _interactionApi,
            "Sorry, but you can only exchange for credits in a guild!", _logger);

        if (member == null) return Result.FromSuccess();

        await _state.AuthenticationService.Login();
        var (isValid, invokerTip) = await Game.ValidatePlayers(
            _context.User.ID.Value,
            new[] { _context.User.ID.Value },
            _state.BurstApi,
            _context,
            10.0f,
            _interactionApi);

        if (!isValid || invokerTip == null)
        {
            var editResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
                    "Sorry, but you don't have enough tips to exchange!");
            return editResult.IsSuccess ? Result.FromSuccess() : Result.FromError(editResult);
        }

        var payload = new
        {
            Credit = amount / 10,
            ChannelId = "0"
        };

        var endpoint = $"{_state.Config.LotteryEndpoint}/credit/{_context.User.ID.Value}/plus";
        try
        {
            var response = await endpoint
                .WithHeader("Authorization", $"Bearer {_state.AuthenticationService.Token}")
                .PatchJsonAsync(payload);
            var lotteryCredits = await response.GetJsonAsync<Dictionary<string, dynamic>>();
            response =
                await _state.BurstApi.SendRawRequest<object>($"/tip/{_context.User.ID.Value}", ApiRequestType.Patch,
                    new
                    {
                        adjustment = "Minus",
                        new_amount = amount
                    });
            var newTips = await response.GetJsonAsync<RawTip>();

            var embed = new EmbedBuilder()
                .WithAuthor(member.GetDisplayName(), iconUrl: member.GetAvatarUrl())
                .WithTitle("Exchange Tips")
                .WithDescription(
                    $"You have successfully exchanged {amount / 10} lottery credits with {amount} tips!")
                .WithColour(BurstColor.Burst.ToColor())
                .WithThumbnailUrl(Constants.BurstGold)
                .AddField("Jack of All Trades Balance", newTips.Amount.ToString(CultureInfo.InvariantCulture), true)
                .Entity;
            embed.AddField("Lottery Balance", lotteryCredits["Credits"].ToString(), true);

            var sendResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(_context.ApplicationID,
                    _context.Token,
                    embeds: new[] { embed.Build().Entity });
            return sendResult.IsSuccess ? Result.FromSuccess() : Result.FromError(sendResult);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to exchange tips for lottery credits: {Exception}", ex);
            var editResult = await _interactionApi
                .EditOriginalInteractionResponseAsync(
                    _context.ApplicationID,
                    _context.Token,
                    "Sorry, but looks like I can't exchange lottery credits for you. Did you join the lottery game via <@!737017231522922556> already?");
            return editResult.IsSuccess ? Result.FromSuccess() : Result.FromError(editResult);
        }
    }
}