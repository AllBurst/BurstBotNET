using BurstBotNET;
using BurstBotNET.Commands;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static async Task MainAsync()
{
    var config = Config.LoadConfig();

    if (config == null)
    {
        return;
    }

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
    client.UseInteractivity();

    var gameStates = new GameStates();
    var localizations = new Localizations();
    var commands = new Commands();
    var handlers = new Handlers(gameStates, localizations, commands);
    
    client.Ready += Handlers.HandleReady;
    client.InteractionCreated += handlers.HandleSlashCommands;
    
    await handlers.RegisterSlashCommands(client, config);
}

Console.WriteLine("Hello, World!");