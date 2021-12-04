using BurstBotNET.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Shared.Interfaces;

public interface ISlashCommand
{
    DiscordApplicationCommand Command { get; init; }

    Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state);

    Tuple<DiscordApplicationCommand, Func<DiscordClient, InteractionCreateEventArgs, State, Task>> GetCommandTuple()
        => new(Command, Handle);
}