using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Commands.NinetyNine;

using CommandGroup = Dictionary<string, Func<DiscordClient, InteractionCreateEventArgs, State, Task>>;

public partial class NinetyNine : ISlashCommand
{
    public DiscordApplicationCommand Command { get; init; }

    private readonly CommandGroup _dispatchables;

    public NinetyNine()
    {
        Command = new DiscordApplicationCommand("ninety_nine", "Play a ninety nine-like game with other people.", new[]
        {
            new DiscordApplicationCommandOption("join",
                "Request to be enqueued to the waiting list to match with other players.",
                ApplicationCommandOptionType.SubCommand, options: new[]
                {
                    new DiscordApplicationCommandOption("difficulty",
                        "The difficulty. Players will only have 4 cards instead of 5 in the hard mode.",
                        ApplicationCommandOptionType.String, true, new[]
                        {
                            new DiscordApplicationCommandOptionChoice("normal", "normal"),
                            new DiscordApplicationCommandOptionChoice("hard", "hard")
                        }),
                    new DiscordApplicationCommandOption("variation",
                        "Choose flavors of Ninety-Nine. Available variations: Taiwanese (default), Icelandic, Standard.",
                        ApplicationCommandOptionType.String, false, new[]
                        {
                            new DiscordApplicationCommandOptionChoice("Taiwanese", "Taiwanese"),
                            new DiscordApplicationCommandOptionChoice("Icelandic", "Icelandic"),
                            new DiscordApplicationCommandOptionChoice("Standard", "Standard")
                        }),
                    new DiscordApplicationCommandOption("player2", "(Optional) The 2nd player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player3", "(Optional) The 3rd player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player4", "(Optional) The 4th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player5", "(Optional) The 5th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player6", "(Optional) The 6th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player7", "(Optional) The 7th player you want to invite.",
                        ApplicationCommandOptionType.User, false),
                    new DiscordApplicationCommandOption("player8", "(Optional) The 8th player you want to invite.",
                        ApplicationCommandOptionType.User, false)
                })
        });

        _dispatchables = new CommandGroup
        {
            { "join", Join }
        };
    }

    public async Task Handle(DiscordClient client, InteractionCreateEventArgs e, State state)
        => await _dispatchables[e.Interaction.Data.Options.ElementAt(0).Name].Invoke(client, e, state);

    public override string ToString()
        => "ninety_nine";
}