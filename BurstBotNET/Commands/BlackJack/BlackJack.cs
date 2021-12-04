using BurstBotNET.Shared.Interfaces;
using BurstBotNET.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Newtonsoft.Json;

namespace BurstBotNET.Commands.BlackJack;

using CommandGroup = Dictionary<string, Func<DiscordClient, InteractionCreateEventArgs, State, Task>>;

public partial class BlackJack : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    private static readonly JsonSerializerSettings JsonSerializerSettings = new();
    private readonly CommandGroup _dispatchables;

    static BlackJack()
    {
        JsonSerializerSettings.MissingMemberHandling = MissingMemberHandling.Error;
    }
    
    public BlackJack()
    {
        Command = new DiscordApplicationCommand("blackjack", "Play a black jack-like game with other people.", new[]
        {
            new DiscordApplicationCommandOption("join",
                "Request to be enqueued to the waiting list to match with other players.",
                ApplicationCommandOptionType.SubCommand, options: new[]
                {
                    new DiscordApplicationCommandOption("player2", "(Optional) The 2nd player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player3", "(Optional) The 3rd player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player4", "(Optional) The 4th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player5", "(Optional) The 5th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player6", "(Optional) The 6th player you want to invite.",
                        ApplicationCommandOptionType.User, false)
                })
        });

        _dispatchables = new CommandGroup
        {
            { "join", Join }
        };
    }

    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e,
        State state)
        => await _dispatchables[e.Interaction.Data.Options.ElementAt(0).Name].Invoke(client, e, state);

}