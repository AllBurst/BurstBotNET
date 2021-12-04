using System.Net;
using System.Text;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Models.Data;
using BurstBotNET.Shared.Models.Data.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.Rewards;

public class Rewards
{
    public static async Task<DiscordWebhookBuilder> GetReward(
        DiscordClient client,
        InteractionCreateEventArgs e,
        PlayerRewardType rewardType,
        State state)
    {
        var user = e.Interaction.User;
        var guild = e.Interaction.Guild;
        var member = await guild.GetMemberAsync(user.Id);
        var botUser = client.CurrentUser;

        var response = await state.BurstApi.GetReward(rewardType, user.Id);
        var reply = new DiscordWebhookBuilder();

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
                var (description, color) = BuildDescription(member.DisplayName, rewardType, typeText, rewardResponse);
                reply = reply
                    .AddEmbed(new DiscordEmbedBuilder()
                        .WithAuthor(member.DisplayName, iconUrl: member.GetAvatarUrl(ImageFormat.Auto))
                        .WithTitle($"{typeText} Reward")
                        .WithColor(color)
                        .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
                        .WithDescription(description)
                        .AddField("Current Tips", rewardResponse.Amount.ToString()));
                break;
            }
            case HttpStatusCode.NotFound:
            {
                reply = reply.WithContent(
                    "Sorry! But have you already opted in?\nIf not, please use `/start` command so we can enroll you!");
                break;
            }
            default:
            {
                reply = reply.WithContent(
                    $"Status value: {response.ResponseMessage.StatusCode.ToString()}\nMessage: {response.ResponseMessage.ReasonPhrase}");
                break;
            }
        }

        return reply;
    }

    private static Tuple<string, DiscordColor> BuildDescription(
        string userDisplayName,
        PlayerRewardType rewardType,
        string typeText,
        RewardResponse tip)
    {
        if (tip.Type.Equals("Success"))
        {
            return new Tuple<string, DiscordColor>(
                $"Congratulations, {userDisplayName}! You have successfully collected your {typeText.ToLowerInvariant()} reward!",
                new DiscordColor((int)BurstColor.Burst));
        }

        var baseText = new StringBuilder("Sorry, but looks like you have already collected your reward recently!\nTime left: ");
        var nextRewardTime = DateTime.Parse(rewardType switch
        {
            PlayerRewardType.Daily => tip.NextDailyReward ?? string.Empty,
            PlayerRewardType.Weekly => tip.NextWeeklyReward ?? string.Empty,
            _ => string.Empty
        });
        return new Tuple<string, DiscordColor>(
            baseText.Append(BuildRemainingTimeMessage(rewardType, nextRewardTime)).ToString(), DiscordColor.Red);
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
                    $"{days} Days {hours.ToString().PadLeft(2)}:{minutes.ToString().PadLeft(2)}:{seconds.ToString().PadLeft(2)}";
            }
            default:
                return string.Empty;
        }
    }
}