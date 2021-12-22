using System.Collections.Immutable;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.Help;

public partial class Help
{
    public static async Task GenericBlackJackHelp(DiscordChannel textChannel, string? message,
        Localizations localizations)
    {
        if (message == null)
            return;

        var split = message
            .Split(' ')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToImmutableList();

        if (split[0] != "help")
            return;

        var command = split.Count <= 1 ? split[0] : split[0] + split[1];
        var result = localizations
            .GetLocalization()
            .BlackJack
            .CommandList
            .TryGetValue(command, out var commandHelpText);
        if (!result)
            return;

        await textChannel.SendMessageAsync(commandHelpText);
    }
    
    private static async Task BlackJackHelp(BaseDiscordClient client, InteractionCreateEventArgs e, State state)
    {
        var botUser = client.CurrentUser;
        var guild = e.Interaction.Guild;
        var member = await guild.GetMemberAsync(e.Interaction.User.Id);

        await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithAuthor(member.DisplayName, iconUrl: member.GetAvatarUrl(ImageFormat.Auto))
                    .WithColor((int)BurstColor.Burst)
                    .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
                    .WithTitle("Black Jack")
                    .WithDescription(state.Localizations.GetLocalization().BlackJack.CommandList["help"])));
    }
}