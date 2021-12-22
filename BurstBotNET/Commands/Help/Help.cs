using System.Collections.Immutable;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.Help;

using CommandGroup = Dictionary<string, Func<BaseDiscordClient, InteractionCreateEventArgs, State, Task>>;

public partial class Help : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    private readonly CommandGroup _dispatchables;

    public Help()
    {
        Command = new DiscordApplicationCommand("help", "Show help and guide of each game.", new[]
        {
            new DiscordApplicationCommandOption("game_name",
                "The game of which you want to show help/guide.",
                ApplicationCommandOptionType.String,
                false,
                new[]
                {
                    new DiscordApplicationCommandOptionChoice("blackjack", "blackjack")
                })
        });

        _dispatchables = new CommandGroup
        {
            { "blackjack", BlackJackHelp }
        };
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        if (e.Interaction.Data.Options != null && e.Interaction.Data.Options.Any())
        {
            var options = e.Interaction.Data.Options.ToImmutableList();
            var gameName = (string)options[0].Value;
            await _dispatchables[gameName].Invoke(client, e, state);
            return;
        }

        var botUser = client.CurrentUser;
        var guild = e.Interaction.Guild;
        var member = await guild.GetMemberAsync(e.Interaction.User.Id);

        await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithAuthor(member.DisplayName, iconUrl: member.GetAvatarUrl(ImageFormat.Auto))
                    .WithColor((int)BurstColor.Burst)
                    .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
                    .WithTitle("📋Command List")
                    .WithDescription(state.Localizations.GetLocalization().Bot)));
    }

    public override string ToString() => "help";
}