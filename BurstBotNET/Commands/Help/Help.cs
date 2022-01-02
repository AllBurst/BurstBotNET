using System.ComponentModel;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Results;

namespace BurstBotNET.Commands.Help;

#pragma warning disable CA2252
public class Help : CommandGroup
{
    public Help()
    {
        
    }

    [Command("help")]
    [Description("Show help and guide of each game.")]
    public async Task<Result> Handle(
        [Description("Show help and guide of each game.")]
        GameType gameName = GameType.None
        )
    {
        return Result.FromSuccess();
    }

    public override string ToString() => "help";
}