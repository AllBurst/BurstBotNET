using System.Collections.Immutable;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;
using ApplicationCommandOptionType = DSharpPlus.ApplicationCommandOptionType;

namespace BurstBotNET.Commands.Help;

#pragma warning disable CA2252
public partial class Help : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

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
                    new DiscordApplicationCommandOptionChoice("blackjack", "blackjack"),
                    new DiscordApplicationCommandOptionChoice("chinese poker", "chinese_poker"),
                    new DiscordApplicationCommandOptionChoice("ninety nine", "ninety_nine")
                })
        });
    }
    
    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        if (e.Interaction.Data.Options != null && e.Interaction.Data.Options.Any())
        {
            var options = e.Interaction.Data.Options.ToImmutableArray();
            var gameName = (string)options[0].Value;
            var localization = state.Localizations.GetLocalization();
            switch (gameName)
            {
                case "blackjack":
                    await GenericHelp(client, e, BlackJack.BlackJack.GameName, localization.BlackJack);
                    break;
                case "chinese_poker":
                    await GenericHelp(client, e, ChinesePoker.ChinesePoker.GameName, localization.ChinesePoker);
                    break;
            }
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
    
    public static async Task<bool> GenericCommandHelp<T>(
        DiscordChannel textChannel,
        ImmutableArray<string> splitMessage,
        ILocalization<T> localization) where T : class
    {
        if (splitMessage.IsEmpty)
            return false;
        
        if (splitMessage[0] != "help")
            return false;

        var command = splitMessage.Length <= 1 ? splitMessage[0] : splitMessage[0] + splitMessage[1];
        var result = localization
            .AvailableCommands
            .TryGetValue(command, out var commandHelpText);
        if (!result)
            return false;

        await textChannel.SendMessageAsync(commandHelpText);
        return true;
    }

    public override string ToString() => "help";
    
    private static async Task GenericHelp<T>(
        BaseDiscordClient client,
        InteractionCreateEventArgs e,
        string gameName,
        ILocalization<T> localization) where T : class
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
                    .WithTitle(gameName)
                    .WithDescription(localization.AvailableCommands["help"])));
    }

    public static string Name => throw new NotImplementedException();

    public static string Description => throw new NotImplementedException();

    public static ImmutableArray<IApplicationCommandOption> ApplicationCommandOptions => throw new NotImplementedException();

    public static Tuple<string, string, ImmutableArray<IApplicationCommandOption>> GetCommandTuple()
    {
        throw new NotImplementedException();
    }

    public Task<IResult> Handle()
    {
        throw new NotImplementedException();
    }
}