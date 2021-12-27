using System.Collections.Immutable;
using System.Text;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Utilities;

public static class Utilities
{
    private static readonly ImmutableArray<char> RandomCharacters =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray().ToImmutableArray();
    
    public static DiscordEmbed BuildGameEmbed(DiscordMember invokingMember, DiscordUser botUser,
        GenericJoinStatus joinStatus, string gameName, string description, int? secondsLeft)
    {
        var playerIds = joinStatus.PlayerIds
            .Select(id => $"💠<@!{id}>")
            .ToImmutableArray();
        var actualDescription = "Joined players: \n" + string.Join('\n', playerIds) + description;
        var title = joinStatus.StatusType switch
        {
            GenericJoinStatusType.Start => $"{invokingMember.DisplayName} has started a {gameName} game!",
            GenericJoinStatusType.Matched =>
                $"{invokingMember.DisplayName}, you have successfully joined a {gameName} game!",
            _ => ""
        };

        return new DiscordEmbedBuilder()
            .WithAuthor(invokingMember.DisplayName, iconUrl: invokingMember.GetAvatarUrl(ImageFormat.Auto))
            .WithTitle(title)
            .WithColor((int)BurstColor.Burst)
            .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
            .WithDescription(actualDescription)
            .WithFooter(joinStatus.StatusType == GenericJoinStatusType.Start
                ? $"React below to confirm to join! {secondsLeft ?? 30} seconds left!"
                : "Starting game...");
    }

    public static string GenerateRandomString(int length = 30)
    {
        var builder = new StringBuilder();
        var random = new Random();
        for (var i = 0; i < length; i++)
            builder.Append(RandomCharacters[random.Next(0, RandomCharacters.Length)]);
        return builder.ToString();
    }
}