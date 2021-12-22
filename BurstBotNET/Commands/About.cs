using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

public class About : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    private const string AboutTextPath = "Assets/localization/bot/about.txt";
    private static readonly Lazy<string> AboutText = new(() => File.ReadAllText(AboutTextPath));

    public About()
    {
        Command = new DiscordApplicationCommand("about", "Show information about All Burst bot.");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e,
        State state)
    {
        var botUser = client.CurrentUser;
        
        await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithAuthor("Jack of All Trades", iconUrl: botUser.GetAvatarUrl(ImageFormat.Auto))
                    .WithColor((int)BurstColor.Burst)
                    .WithThumbnail(Constants.BurstLogo)
                    .WithDescription(AboutText.Value)
                    .WithFooter("All Burst: Development 1.0 | 2021-12-14")
                    .Build()));
    }

    public override string ToString() => "about";
}