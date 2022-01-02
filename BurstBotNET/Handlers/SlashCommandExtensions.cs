using BurstBotNET.Commands;
using BurstBotNET.Commands.BlackJack;
using BurstBotNET.Commands.ChinesePoker;
using BurstBotNET.Commands.NinetyNine;
using BurstBotNET.Commands.Rewards;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using Microsoft.Extensions.DependencyInjection;
using Remora.Commands.Extensions;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Interactivity.Extensions;
using Remora.Discord.Pagination.Extensions;

namespace BurstBotNET.Handlers;

#pragma warning disable CA2252
public static class SlashCommandExtensions
{
    public static IServiceCollection AddSlashCommands(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddCommandGroup<About>()
            .AddCommandGroup<Balance>()
            .AddCommandGroup<Daily>()
            .AddCommandGroup<Ping>()
            .AddCommandGroup<Start>()
            .AddCommandGroup<Weekly>()
            .AddCommandGroup<BlackJack>()
            .AddCommandGroup<ChinesePoker>()
            .AddCommandGroup<NinetyNine>()
            .AddPagination()
            .AddInteractiveEntity<ChinesePokerDropDownEntity>()
            .AddInteractiveEntity<ChinesePokerButtonEntity>();
    }

    /*private async Task CreateGlobalCommands(DiscordClient client, bool forceRecreate)
    {
        var globalCommandsToCreate = _commands
            .GlobalCommands
            .Select(pair => pair.Value.Item1)
            .ToImmutableArray();

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
                .ToImmutableArray();
            var commandsToCreate = globalCommandsToCreate
                .Where(cmd => !existingGlobalCommandNames.Contains(cmd.Name))
                .ToImmutableArray();
            foreach (var cmd in commandsToCreate) await client.CreateGlobalApplicationCommandAsync(cmd);
        }
    }

    private async Task CreateGuildCommands(DiscordClient client, Config config)
    {
        var guildIds = config.TestGuilds.Select(ulong.Parse).ToImmutableArray();
        var guildCommandsToCreate = _commands
            .GuildCommands
            .Select(pair => pair.Value.Item1)
            .ToImmutableArray();

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
                    .ToImmutableArray();

                var commandsToCreate = guildCommandsToCreate
                    .Where(cmd => !existingGuildCommandNames.Contains(cmd.Name))
                    .ToImmutableArray();

                foreach (var cmd in commandsToCreate) await client.CreateGuildApplicationCommandAsync(guildId, cmd);
            }
        }
    }*/
}