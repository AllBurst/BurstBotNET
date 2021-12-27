using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Config;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Handlers;

#pragma warning disable CA2252
public partial class Handlers
{
    public Task HandleSlashCommands(DiscordClient client, InteractionCreateEventArgs e)
    {
        if (!e.Interaction.GuildId.HasValue)
            return Task.CompletedTask;

        var result = _commands.GlobalCommands
            .TryGetValue(e.Interaction.Data.Name, out var globalCommand);
        if (result)
        {
            _ = Task.Run(async () => await globalCommand!.Item2.Invoke(client, e, _state));
            return Task.CompletedTask;
        }

        result = _commands.GuildCommands
            .TryGetValue(e.Interaction.Data.Name, out var guildCommand);
        if (result)
            _ = Task.Run(async () => await guildCommand!.Item2.Invoke(client, e, _state));
        
        return Task.CompletedTask;
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
            foreach (var cmd in globalCommands) await client.DeleteGlobalApplicationCommandAsync(cmd?.Id ?? 0);

            foreach (var cmd in globalCommandsToCreate) await client.CreateGlobalApplicationCommandAsync(cmd);
        }
        else
        {
            var existingGlobalCommandNames = globalCommands
                .Select(cmd => cmd.Name)
                .ToImmutableList();
            var commandsToCreate = globalCommandsToCreate
                .Where(cmd => !existingGlobalCommandNames.Contains(cmd.Name))
                .ToImmutableList();
            foreach (var cmd in commandsToCreate) await client.CreateGlobalApplicationCommandAsync(cmd);
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
                foreach (var cmd in guildCommands) await client.DeleteGuildApplicationCommandAsync(guildId, cmd.Id);

                foreach (var cmd in guildCommandsToCreate)
                    await client.CreateGuildApplicationCommandAsync(guildId, cmd);
            }
            else
            {
                var existingGuildCommandNames = guildCommands
                    .Select(cmd => cmd.Name)
                    .ToImmutableList();

                var commandsToCreate = guildCommandsToCreate
                    .Where(cmd => !existingGuildCommandNames.Contains(cmd.Name))
                    .ToImmutableList();

                foreach (var cmd in commandsToCreate) await client.CreateGuildApplicationCommandAsync(guildId, cmd);
            }
        }
    }
}