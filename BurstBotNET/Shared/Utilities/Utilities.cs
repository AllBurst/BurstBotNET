using System.Collections.Immutable;
using BurstBotNET.Shared.Models.Game.BlackJack.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;

namespace BurstBotNET.Shared.Utilities;

public static class Utilities
{
    public static DiscordEmbed BuildBlackJackEmbed(DiscordMember invokingMember, DiscordUser botUser,
        BlackJackJoinStatus joinStatus, string description, int? secondsLeft)
    {
        var playerIds = joinStatus.PlayerIds
            .Select(id => $"💠<@!{id}>")
            .ToImmutableList();
        var actualDescription = "Joined players: \n" + string.Join('\n', playerIds) + description;
        var title = joinStatus.StatusType switch
        {
            BlackJackJoinStatusType.Start => $"{invokingMember.DisplayName} has started a Black Jack game!",
            BlackJackJoinStatusType.Matched =>
                $"{invokingMember.DisplayName}, you have successfully joined a Black Jack game!",
            _ => ""
        };

        return new DiscordEmbedBuilder()
            .WithAuthor(invokingMember.DisplayName, iconUrl: invokingMember.GetAvatarUrl(ImageFormat.Auto))
            .WithTitle(title)
            .WithColor((int)BurstColor.Burst)
            .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
            .WithDescription(actualDescription)
            .WithFooter(joinStatus.StatusType == BlackJackJoinStatusType.Start
                ? $"React below to confirm to join! {secondsLeft ?? 30} seconds left!"
                : "Starting game...");
    }
}