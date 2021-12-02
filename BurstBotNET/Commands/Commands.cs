using BurstBotNET.Api;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

using CommandGroup = Dictionary<string, Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, Config, GameStates, BurstApi, Localizations, Task>>>;

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
                        Func<DiscordClient, InteractionCreateEventArgs, Config, GameStates, BurstApi, Localizations, Task>>(
                        about.Command,
                        (client, e, config, gameStates, burstApi, localizations) =>
                            about.Handle(client, e, config, gameStates, burstApi, localizations))
                },
                {
                    "ping",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, Config, GameStates, BurstApi, Localizations, Task>>(
                        ping.Command,
                        (client, e, config, gameStates, burstApi, localizations) =>
                            ping.Handle(client, e, config, gameStates, burstApi, localizations))
                }
            };

        var blackJack = new BlackJack.BlackJack();

        GuildCommands =
            new CommandGroup
            {
                {
                    "blackjack",
                    new Tuple<DiscordApplicationCommand,
                        Func<DiscordClient, InteractionCreateEventArgs, Config, GameStates, BurstApi, Localizations, Task>>(
                        blackJack.Command, (client, e, config, gameStates, burstApi, localizations) => blackJack.Handle(client, e, config, gameStates, burstApi, localizations))
                }
            };
    }
}