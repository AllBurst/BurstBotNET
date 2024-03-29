﻿using System.Collections.Immutable;
using BurstBotNET.Handlers;
using BurstBotShared.Api;
using BurstBotShared.Services;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Remora.Discord.API;
using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Commands.Extensions;
using Remora.Discord.Commands.Services;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Hosting.Extensions;
using Remora.Discord.Interactivity.Extensions;
using Constants = BurstBotShared.Shared.Constants;

#pragma warning disable CA2252

namespace BurstBotNET
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var config = Config.LoadConfig();

            if (config == null)
                return;

            var shutdownTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownTokenSource.Cancel();
            };
            
            var host = CreateHostBuilder(args, config)
                .UseConsoleLifetime()
                .Build();
            var services = host.Services;
            var logger = services.GetRequiredService<ILogger<Program>>();
            var slashService = services.GetRequiredService<SlashService>();
            logger.LogInformation("Processor count: {Count}", Environment.ProcessorCount);

            var checkSlashSupport = slashService.SupportsSlashCommands();
            if (!checkSlashSupport.IsSuccess)
            {
                logger.LogWarning("The registered commands of the bot don't support slash commands: {Reason}",
                    checkSlashSupport.Error?.Message);
            }
            else
            {
                var testGuilds = config
                    .TestGuilds
                    .Select(ulong.Parse)
                    .Where(id => id != 0)
                    .ToImmutableArray();

                if (testGuilds.Any())
                {
                    var snowflakes = testGuilds
                        .Select(DiscordSnowflake.New)
                        .ToImmutableArray();
                    foreach (var guild in snowflakes)
                    {
                        var updateResult = await slashService.UpdateSlashCommandsAsync(guild, ct: shutdownTokenSource.Token);
                        if (!updateResult.IsSuccess)
                        {
                            logger.LogError("Failed to update slash commands: {Reason}, inner: {Inner}",
                                updateResult.Error, updateResult.Inner);
                        }
                    }
                }
                else
                {
                    var updateResult = await slashService.UpdateSlashCommandsAsync(null, ct: shutdownTokenSource.Token);
                    if (!updateResult.IsSuccess)
                    {
                        logger.LogError("Failed to update slash commands: {Reason}, inner: {Inner}",
                            updateResult.Error, updateResult.Inner);
                    }
                }
            }

            await host.RunAsync(shutdownTokenSource.Token);
        }
        
        private static IHostBuilder CreateHostBuilder(string[] args, Config config)
        {
            var gameStates = new GameStates();
            var localizations = new Localizations();
            var burstApi = new BurstApi(config);
            var deckService = new DeckService();
            var amqpService = new AmqpService(config);
            var authenticationService = new AuthenticationService(config);
            var state = new State
            {
                BurstApi = burstApi,
                Config = config,
                GameStates = gameStates,
                Localizations = localizations,
                DeckService = deckService,
                AmqpService = amqpService,
                AuthenticationService = authenticationService
            };

            return Host.CreateDefaultBuilder(args)
                .AddDiscordService(_ => config.Token)
                .ConfigureServices((_, services) => services
                    .AddDiscordCommands(true)
                    .AddInteractivity()
                    .AddSingleton(state)
                    .AddResponder<ReadyResponder>()
                    .AddResponder<MessageResponder>()
                    .AddSlashCommands()
                    .Configure<DiscordGatewayClientOptions>(opt =>
                    {
                        opt.Intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages |
                                      GatewayIntents.GuildMessageReactions;
                        opt.Presence = new UpdatePresence(ClientStatus.Online, false, null,
                            new[] { Constants.Activities.Choose() });
                    }))
                .ConfigureLogging(builder => builder
                    .AddConsole()
                    .AddFilter("System.Net.Http.HttpClient.*.LogicalHandler", LogLevel.Warning)
                    .AddFilter("System.Net.Http.HttpClient.*.ClientHandler", LogLevel.Warning)
                    .AddFilter("BurstBotNET.*", LogLevel.Debug)
                    .AddFilter("BurstBotShared.*", LogLevel.Debug));
        }
    }
}