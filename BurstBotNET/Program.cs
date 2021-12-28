using BurstBotNET.Commands;
using BurstBotNET.Handlers;
using BurstBotShared.Api;
using BurstBotShared.Services;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2252
static async Task MainAsync()
{
    var config = Config.LoadConfig();

    if (config == null)
        return;

    var configuration = new DiscordConfiguration
    {
        Token = config.Token,
        TokenType = TokenType.Bot,
        AutoReconnect = true,
        Intents = DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.GuildMessageReactions,
        MinimumLogLevel = config.LogLevel switch
        {
            "DEBUG" => LogLevel.Debug,
            "TRACE" => LogLevel.Trace,
            "WARN" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "INFO" => LogLevel.Information,
            "NONE" => LogLevel.None,
            "CRITICAL" => LogLevel.Critical,
            _ => LogLevel.Debug
        }
    };

    var services = new ServiceCollection()
        .AddSingleton<Random>()
        .AddSingleton<HttpClient>()
        .BuildServiceProvider();

    var commandsConfiguration = new CommandsNextConfiguration
    {
        Services = services,
        EnableDms = false,
        EnableMentionPrefix = false,
        CaseSensitive = false,
    };

    var client = new DiscordClient(configuration);
    client.UseCommandsNext(commandsConfiguration);
    client.UseInteractivity(new InteractivityConfiguration
    {
        Timeout = TimeSpan.FromSeconds(config.Timeout * 2)
    });
    
    var gameStates = new GameStates();
    var localizations = new Localizations();
    var commands = new Commands();
    var burstApi = new BurstApi(config);
    var deckService = new DeckService();
    var handlers = new Handlers(commands, new State
    {
        BurstApi = burstApi,
        Config = config,
        GameStates = gameStates,
        Localizations = localizations,
        DeckService = deckService
    });
    
    client.Ready += Handlers.HandleReady;
    client.InteractionCreated += handlers.HandleSlashCommands;
    client.ClientErrored += handlers.HandleClientError;
    client.SocketErrored += handlers.HandleSocketError;
    client.MessageCreated += handlers.HandleMessage;
    
    await client.ConnectAsync(new DiscordActivity("Black Jack", ActivityType.Playing), UserStatus.Online);
    await handlers.RegisterSlashCommands(client, config);
    await Task.Delay(-1);
}

MainAsync().GetAwaiter().GetResult();