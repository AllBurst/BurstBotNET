using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

public class Balance : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    public Balance()
    {
        Command = new DiscordApplicationCommand("balance",
            "Check how many tips you currently have.");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var user = e.Interaction.User;
        var guild = e.Interaction.Guild;
        var member = await guild.GetMemberAsync(user.Id);

        var playerData = await state.BurstApi.SendRawRequest<object>(
            $"/player/{user.Id}", ApiRequestType.Get, null);
        if (!playerData.ResponseMessage.IsSuccessStatusCode)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent("Sorry! But have you already opted in?\nIf not, please use `/start` command so we can enroll you!"));
            return;
        }

        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
            .AddEmbed(new DiscordEmbedBuilder()
                .WithAuthor(member.DisplayName, iconUrl: member.GetAvatarUrl(ImageFormat.Auto))
                .WithColor((int)BurstColor.Burst)
                .WithDescription("Here is your account balance.")
                .WithThumbnail(client.CurrentUser.GetAvatarUrl(ImageFormat.Auto))
                .WithTitle("Account Balance")
                .AddField("Current Tips", (await playerData.GetJsonAsync<RawPlayer>()).Amount.ToString())));
    }

    public override string ToString() => "balance";
}