using BurstBotNET.Shared.Interfaces;
using BurstBotNET.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.Rewards;

public class Weekly : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    public Weekly()
    {
        Command = new DiscordApplicationCommand("weekly", "Get your weekly reward of 70 tips here.");
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        await e.Interaction.EditOriginalResponseAsync(await Rewards.GetReward(client, e, PlayerRewardType.Weekly,
            state));
    }

    public override string ToString() => "weekly";
}