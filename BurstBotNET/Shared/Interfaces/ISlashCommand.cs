using BurstBotNET.Shared.Models.Game;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Shared.Interfaces;

public interface ISlashCommand
{
    DiscordApplicationCommand Command { get; init; }
    Task Handle(DiscordClient client, InteractionCreateEventArgs e, GameStates gameStates);
}