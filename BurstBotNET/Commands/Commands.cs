using BurstBotNET.Commands.Rewards;
using BurstBotNET.Shared.Interfaces;
using BurstBotNET.Shared.Models.Data;
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
        var balance = new Balance();
        var start = new Start();
        var daily = new Daily();
        var weekly = new Weekly();

        GlobalCommands =
            new CommandGroup
            {
                { about.ToString(), ((ISlashCommand)about).GetCommandTuple() },
                { ping.ToString(), ((ISlashCommand)ping).GetCommandTuple() },
                { balance.ToString(), ((ISlashCommand)balance).GetCommandTuple() },
                { start.ToString(), ((ISlashCommand)start).GetCommandTuple() },
                { daily.ToString(), ((ISlashCommand)daily).GetCommandTuple() },
                { weekly.ToString(), ((ISlashCommand)weekly).GetCommandTuple() }
            };

        var blackJack = new BlackJack.BlackJack();
        var help = new Help.Help();

        GuildCommands =
            new CommandGroup
            {
                { blackJack.ToString(), ((ISlashCommand)blackJack).GetCommandTuple() },
                { help.ToString(), ((ISlashCommand)help).GetCommandTuple() }
            };
    }
}