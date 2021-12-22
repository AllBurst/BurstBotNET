using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.Rewards;

public class Daily : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    public Daily()
    {
        Command = new DiscordApplicationCommand("daily", "Get your daily reward of 10 tips here.");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        await e.Interaction.EditOriginalResponseAsync(await Rewards.GetReward(client, e, PlayerRewardType.Daily,
            state));
    }

    public override string ToString() => "daily";
}