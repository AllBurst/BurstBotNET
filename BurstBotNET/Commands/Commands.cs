using BurstBotNET.Commands.Rewards;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands;

using CommandGroup =
    Dictionary<string, Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, State, Task>>>;

public class Commands
{
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
        var ninetyNine = new NinetyNine.NinetyNine();
        var help = new Help.Help();
        var chinesePoker = new ChinesePoker.ChinesePoker();

        GuildCommands =
            new CommandGroup
            {
                { blackJack.ToString(), ((ISlashCommand)blackJack).GetCommandTuple() },
                { ninetyNine.ToString(), ((ISlashCommand)ninetyNine).GetCommandTuple() },
                { help.ToString(), ((ISlashCommand)help).GetCommandTuple() },
                { chinesePoker.ToString(), ((ISlashCommand)chinesePoker).GetCommandTuple() }
            };
    }

    public CommandGroup GlobalCommands { get; }

    public CommandGroup GuildCommands { get; }
}