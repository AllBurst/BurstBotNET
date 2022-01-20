using System.Drawing;
using System.Net;
using System.Text;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.Rewards;

public class Rewards
{
    public static async Task<Result> GetReward(
        InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        PlayerRewardType rewardType,
        State state,
        ILogger logger)
    {
        var bot = await Utilities.GetBotUser(userApi, logger);
        
        if (bot == null) return Result.FromSuccess();

        var member = await Utilities.GetUserMember(context, interactionApi,
            "Sorry, but you can only get your daily/weekly reward in a guild!",
            logger);
        
        if (member == null) return Result.FromSuccess();

        var invokingUser = member.User.Value;
        var response = await state.BurstApi.GetReward(rewardType, invokingUser.ID.Value);

        switch (response.ResponseMessage.StatusCode)
        {
            case HttpStatusCode.OK:
            {
                var rewardResponse = await response.GetJsonAsync<RewardResponse>();
                var typeText = rewardType switch
                {
                    PlayerRewardType.Daily => "Daily",
                    PlayerRewardType.Weekly => "Weekly",
                    _ => ""
                };
                var displayName = member.GetDisplayName();
                var (description, color) = BuildDescription(displayName, rewardType, typeText, rewardResponse);
                var reply = new Embed(
                    $"{typeText} Reward",
                    Author: new EmbedAuthor(displayName, IconUrl: invokingUser.GetAvatarUrl()),
                    Colour: color,
                    Thumbnail: new EmbedThumbnail(Constants.BurstGold),
                    Description: description,
                    Fields: new[] { new EmbedField("Current Tips", rewardResponse.Amount.ToString()) }
                );
                var rewardResult = await interactionApi
                    .EditOriginalInteractionResponseAsync(
                        context.ApplicationID,
                        context.Token,
                        embeds: new[] { reply });
                return rewardResult.IsSuccess ? Result.FromSuccess() : Result.FromError(rewardResult);
            }
            case HttpStatusCode.NotFound:
            {
                var rewardResult = await interactionApi
                    .EditOriginalInteractionResponseAsync(
                        context.ApplicationID,
                        context.Token,
                        "Sorry! But have you already opted in?\nIf not, please use `/start` command so we can enroll you!");
                return rewardResult.IsSuccess ? Result.FromSuccess() : Result.FromError(rewardResult);
            }
            default:
            {
                var rewardResult = await interactionApi
                    .EditOriginalInteractionResponseAsync(
                        context.ApplicationID,
                        context.Token,
                        $"Status value: {response.ResponseMessage.StatusCode.ToString()}\nMessage: {response.ResponseMessage.ReasonPhrase}");
                return rewardResult.IsSuccess ? Result.FromSuccess() : Result.FromError(rewardResult);
            }
        }
    }

    private static Tuple<string, Color> BuildDescription(
        string userDisplayName,
        PlayerRewardType rewardType,
        string typeText,
        RewardResponse tip)
    {
        if (tip.Type.Equals("Success"))
        {
            return new Tuple<string, Color>(
                $"Congratulations, {userDisplayName}! You have successfully collected your {typeText.ToLowerInvariant()} reward!",
                BurstColor.Burst.ToColor());
        }

        var baseText = new StringBuilder("Sorry, but looks like you have already collected your reward recently!\nTime left: ");
        var nextRewardTime = DateTime.Parse(rewardType switch
        {
            PlayerRewardType.Daily => tip.NextDailyReward ?? string.Empty,
            PlayerRewardType.Weekly => tip.NextWeeklyReward ?? string.Empty,
            _ => string.Empty
        });
        baseText.Append(BuildRemainingTimeMessage(rewardType, nextRewardTime) + '\n');
        var unixSeconds = ((DateTimeOffset)nextRewardTime).ToUnixTimeSeconds();
        baseText.Append($"Next Reward Date: <t:{unixSeconds}>");
        return new Tuple<string, Color>(baseText.ToString(), Color.Red);
    }

    private static string BuildRemainingTimeMessage(
        PlayerRewardType rewardType,
        DateTime nextRewardTime)
    {
        var diff = (int)(nextRewardTime - DateTime.Now).TotalSeconds;
        switch (rewardType)
        {
            case PlayerRewardType.Daily:
            {
                var hours = diff / 60 / 60;
                var leftoverSeconds = diff - hours * 60 * 60;
                var minutes = leftoverSeconds / 60;
                var seconds = leftoverSeconds - minutes * 60;
                return $"{hours.ToString().PadLeft(2)}:{minutes.ToString().PadLeft(2)}:{seconds.ToString().PadLeft(2)}";
            }
            case PlayerRewardType.Weekly:
            {
                var days = diff / 60 / 60 / 24;
                var leftoverSeconds = diff - days * 60 * 60 * 24;
                var hours = leftoverSeconds / 60 / 60;
                leftoverSeconds -= hours * 60 * 60;
                var minutes = leftoverSeconds / 60;
                var seconds = leftoverSeconds - minutes * 60;
                return
                    $"{days} Days {hours.ToString().PadLeft(2, '0')}:{minutes.ToString().PadLeft(2, '0')}:{seconds.ToString().PadLeft(2, '0')}";
            }
            default:
                return string.Empty;
        }
    }
}