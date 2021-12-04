using BurstBotNET.Api;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Data;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Shared.Interfaces;

public interface ISlashCommand
{
    DiscordApplicationCommand Command { get; init; }

    Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state);
}