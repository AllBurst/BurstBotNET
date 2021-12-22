using System.Globalization;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.NinetyNine;

public partial class NinetyNine
{
    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;
    
    private async Task Join(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var mentionedPlayers = new List<ulong> { e.Interaction.User.Id };
        var options = e.Interaction.Data.Options
            .ElementAt(0)
            .Options
            .ToArray();
        var difficulty = Enum.Parse<NinetyNineDifficulty>(TextInfo.ToTitleCase((string)options[0].Value));
        var variation = NinetyNineVariation.Taiwanese;
        
        // Difficulty selected and no other options are given.
        if (options.Length <= 1)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"Difficulty: {difficulty}"));
            // TODO: Match
            return;
        }
        
        var otherOptions = options[1..];
        foreach (var opt in otherOptions)
        {
            if (opt.Name == "variation")
                variation = Enum.Parse<NinetyNineVariation>((string)opt.Value);
            else
            {
                mentionedPlayers.Add((ulong)opt.Value);
            }
        }

        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
            .WithContent(
                $"Difficulty: {difficulty}\nVariation: {variation}\nInvited players: {string.Join(' ', mentionedPlayers.Select(p => $"<@!{p}>"))}"));
    }
}