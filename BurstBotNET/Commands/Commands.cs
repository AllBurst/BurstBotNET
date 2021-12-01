using BurstBotNET.Shared.Models.Game;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

public class Commands
{
    public Dictionary<string, Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, GameStates, Task>>> GlobalCommands
    {
        get;
    }
    public Dictionary<string, Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, GameStates, Task>>> GuildCommands
    {
        get;
    }

    public Commands()
    {
        var about = new About();
        var ping = new Ping();

        GlobalCommands =
            new Dictionary<string, Tuple<DiscordApplicationCommand,
                Func<DiscordClient, InteractionCreateEventArgs, GameStates, Task>>>
            {
                {
                    "about",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, GameStates, Task>>(
                        about.Command, about.Handle)
                },
                {
                    "ping",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, GameStates, Task>>(ping.Command, ping.Handle)
                }
            };
    }
}