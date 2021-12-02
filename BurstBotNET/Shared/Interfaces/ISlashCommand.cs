using BurstBotNET.Api;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Shared.Interfaces;

public interface ISlashCommand
{
    DiscordApplicationCommand Command { get; init; }

    Task Handle(DiscordClient client, InteractionCreateEventArgs e, Config config, GameStates gameStates,
        BurstApi burstApi,
        Localizations localizations);
}