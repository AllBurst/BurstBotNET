using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.NinetyNine;

[Group("ninety_nine")]
[Description("Play a ninety nine-like game with other people.")]
public partial class NinetyNine : CommandGroup
{
    public const string GameName = "Ninety Nine";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly State _state;
    private readonly ILogger<NinetyNine> _logger;
    
    public NinetyNine(InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        State state,
        ILogger<NinetyNine> logger)
    {
        _context = context;
        _userApi = userApi;
        _interactionApi = interactionApi;
        _guildApi = guildApi;
        _channelApi = channelApi;
        _state = state;
        _logger = logger;
    }

    [Command("join")]
    [Description("Request to be enqueued to the waiting list to match with other players.")]
    public async Task<IResult> Handle(
        [Description("The difficulty. Players will only have 4 cards instead of 5 in the hard mode.")]
        NinetyNineDifficulty difficulty,
        [Description("The base bet. The reward will be the number of players multiplied by this.")]
        float baseBet = 1.0f,
        [Description("Choose flavors of Ninety-Nine. Available variations: Taiwanese (default), Icelandic, Standard.")]
        NinetyNineVariation variation = NinetyNineVariation.Taiwanese,
        [Description("(Optional) The 2nd player you want to invite.")]
        IUser? player2 = null,
        [Description("(Optional) The 3rd player you want to invite.")]
        IUser? player3 = null,
        [Description("(Optional) The 4th player you want to invite.")]
        IUser? player4 = null,
        [Description("(Optional) The 5th player you want to invite.")]
        IUser? player5 = null,
        [Description("(Optional) The 6th player you want to invite.")]
        IUser? player6 = null,
        [Description("(Optional) The 7th player you want to invite.")]
        IUser? player7 = null,
        [Description("(Optional) The 8th player you want to invite.")]
        IUser? player8 = null
    ) => await Join(baseBet, difficulty, variation, player2, player3, player4, player5, player6, player7, player8);

    public override string ToString()
        => "ninety_nine";
}