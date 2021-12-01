using System.Collections.Immutable;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BurstBotNET;

public class Handlers
{
    private readonly GameStates _gameStates;
    private readonly Localizations _localizations;
    private readonly Commands.Commands _commands;

    public Handlers(GameStates gameStates, Localizations localizations, Commands.Commands commands)
    {
        _gameStates = gameStates;
        _localizations = localizations;
        _commands = commands;
    }
    
    public static async Task HandleReady(DiscordClient client, ReadyEventArgs e)
    {
        client.Logger.LogInformation("Successfully connected to the gateway");
        client.Logger.LogInformation("{Name}#{Discriminator} is now online", client.CurrentUser.Username,
            client.CurrentUser.Discriminator);
    }

    public async Task HandleSlashCommands(DiscordClient client, InteractionCreateEventArgs e)
    {
        if (!e.Interaction.GuildId.HasValue)
            return;

        var result = _commands.GlobalCommands
            .TryGetValue(e.Interaction.Data.Name, out var globalCommand);
        if (result)
        {
            await globalCommand!.Item2.Invoke(client, e, _gameStates);
            return;
        }

        result = _commands.GuildCommands
            .TryGetValue(e.Interaction.Data.Name, out var guildCommand);
        if (result)
            await guildCommand!.Item2.Invoke(client, e, _gameStates);
    }

    public async Task RegisterSlashCommands(DiscordClient client, Config config)
    {
        await CreateGuildCommands(client, config);
        await CreateGlobalCommands(client, config.RecreateGlobals);
    }

    private async Task CreateGlobalCommands(DiscordClient client, bool forceRecreate)
    {
        var globalCommandsToCreate = _commands
            .GlobalCommands
            .Select(pair => pair.Value.Item1)
            .ToImmutableList();

        var globalCommands = await client.GetGlobalApplicationCommandsAsync();

        if (forceRecreate)
        {
            foreach (var cmd in globalCommands)
            {
                await client.DeleteGlobalApplicationCommandAsync(cmd?.Id ?? 0);
            }
            
            foreach (var cmd in globalCommandsToCreate)
            {
                await client.CreateGlobalApplicationCommandAsync(cmd);
            }
        }
        else
        {
            var existingGlobalCommandNames = globalCommands
                .Select(cmd => cmd.Name)
                .ToImmutableList();
            var commandsToCreate = globalCommandsToCreate
                .Where(cmd => !existingGlobalCommandNames.Contains(cmd.Name))
                .ToImmutableList();
            foreach (var cmd in commandsToCreate)
            {
                await client.CreateGlobalApplicationCommandAsync(cmd);
            }
        }
    }

    private async Task CreateGuildCommands(DiscordClient client, Config config)
    {
        var guildIds = config.TestGuilds.Select(ulong.Parse).ToImmutableList();
        var guildCommandsToCreate = _commands
            .GuildCommands
            .Select(pair => pair.Value.Item1)
            .ToImmutableList();

        foreach (var guildId in guildIds)
        {
            var guildCommands = await client.GetGuildApplicationCommandsAsync(guildId);
            if (config.RecreateGuilds)
            {
                foreach (var cmd in guildCommands)
                {
                    await client.DeleteGuildApplicationCommandAsync(guildId, cmd.Id);
                }

                foreach (var cmd in guildCommandsToCreate)
                {
                    await client.CreateGuildApplicationCommandAsync(guildId, cmd);
                }
            }
            else
            {
                var existingGuildCommandNames = guildCommands
                    .Select(cmd => cmd.Name)
                    .ToImmutableList();

                var commandsToCreate = guildCommandsToCreate
                    .Where(cmd => !existingGuildCommandNames.Contains(cmd.Name))
                    .ToImmutableList();

                foreach (var cmd in commandsToCreate)
                {
                    await client.CreateGuildApplicationCommandAsync(guildId, cmd);
                }
            }
        }
    }
}