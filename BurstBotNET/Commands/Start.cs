using BurstBotNET.Api;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Interfaces;
using BurstBotNET.Shared.Models.Data;
using BurstBotNET.Shared.Models.Data.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

public class Start : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    public Start()
    {
        Command = new DiscordApplicationCommand("start", "Opt-in and create an account to start joining games!");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var user = e.Interaction.User;
        var guild = e.Interaction.Guild;
        var member = await guild.GetMemberAsync(user.Id);
        var newPlayer = new NewPlayer
        {
            PlayerId = user.Id.ToString(),
            Name = member.DisplayName,
            AvatarUrl = member.GetAvatarUrl(ImageFormat.Auto)
        };

        var playerData = await state.BurstApi.SendRawRequest("/player", ApiRequestType.Post, newPlayer);
        var botUser = client.CurrentUser;

        if (!playerData.ResponseMessage.IsSuccessStatusCode)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent("Sorry! But you seem to have already joined!"));
            return;
        }

        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
            .AddEmbed(new DiscordEmbedBuilder()
                .WithAuthor(member.DisplayName, iconUrl: member.GetAvatarUrl(ImageFormat.Auto))
                .WithColor((int)BurstColor.Burst)
                .WithDescription(
                    "Congratulations! You can now start joining games to play poker with other people!\nAs a first time bonus, **you also have received 100 tips!**\nEnjoy!")
                .WithThumbnail(botUser.GetAvatarUrl(ImageFormat.Auto))
                .WithTitle("Welcome to the All Burst!")
                .AddField("Current Tips", (await playerData.GetJsonAsync<RawPlayer>()).Amount.ToString())));
    }

    public override string ToString() => "start";
}