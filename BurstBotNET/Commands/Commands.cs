using BurstBotNET.Api;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Data;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

using CommandGroup = Dictionary<string, Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, State, Task>>>;

public class Commands
{
    public CommandGroup GlobalCommands
    {
        get;
    }
    public CommandGroup GuildCommands
    {
        get;
    }

    public Commands()
    {
        var about = new About();
        var ping = new Ping();

        GlobalCommands =
            new CommandGroup
            {
                {
                    "about",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, State, Task>>(
                        about.Command,
                        (client, e, state) =>
                            about.Handle(client, e, state))
                },
                {
                    "ping",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, State, Task>>(
                        ping.Command,
                        (client, e, state) =>
                            ping.Handle(client, e, state))
                }
            };

        var blackJack = new BlackJack.BlackJack();

        GuildCommands =
            new CommandGroup
            {
                {
                    "blackjack",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, State, Task>>(
                        blackJack.Command, (client, e, state) => blackJack.Handle(client, e, state))
                }
            };
    }
}