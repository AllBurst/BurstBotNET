using System.Collections.Immutable;
using BurstBotNET.Commands.Rewards;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotNET.Commands;

#pragma warning disable CA2252
public static class Commands
{
    /*public Commands()
    {
        var blackJack = new BlackJack.BlackJack();
        var ninetyNine = new NinetyNine.NinetyNine();
        var help = new Help.Help();
        var chinesePoker = new ChinesePoker.ChinesePoker();

        var a = About;

        GuildCommands =
            new CommandGroup
            {
                { blackJack.ToString(), ((ISlashCommand)blackJack).GetCommandTuple() },
                { ninetyNine.ToString(), ((ISlashCommand)ninetyNine).GetCommandTuple() },
                { help.ToString(), ((ISlashCommand)help).GetCommandTuple() },
                { chinesePoker.ToString(), ((ISlashCommand)chinesePoker).GetCommandTuple() }
            };
    }*/

    public static readonly List<Tuple<string, string, ImmutableArray<IApplicationCommandOption>>> GlobalCommands
        = new()
        {
            About.GetCommandTuple(),
            Balance.GetCommandTuple(),
            Ping.GetCommandTuple(),
            Start.GetCommandTuple(),
            Daily.GetCommandTuple(),
            Weekly.GetCommandTuple()
        };

    public static readonly List<Tuple<string, string, ImmutableArray<IApplicationCommandOption>>> GuildCommands
        = new()
        {

        };
}