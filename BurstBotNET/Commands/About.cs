using BurstBotNET.Shared;
using BurstBotNET.Shared.Interfaces;
using BurstBotNET.Shared.Models.Game;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

public class About : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    private const string AboutTextPath = "assets/localization/bot/about.txt";
    private static readonly Lazy<string> AboutText = new(() => File.ReadAllText(AboutTextPath));

    public About()
    {
        Command = new DiscordApplicationCommand("about", "Show information about All Burst bot.");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, GameStates gameStates)
    {
        var botUser = client.CurrentUser;
        
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithAuthor("All Burst from Project 21", botUser.GetAvatarUrl(ImageFormat.Auto))
                    .WithColor((int)BurstColor.Kotlin)
                    .WithThumbnail(Constants.BurstLogo)
                    .WithDescription(AboutText.Value)
                    .WithFooter("All Burst: Development 1.0 | 2021-12-01")
                    .Build()));
    }
}