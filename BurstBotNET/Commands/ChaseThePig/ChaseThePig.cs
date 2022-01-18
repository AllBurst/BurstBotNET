using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.ChaseThePig;

[Group("chase_the_pig")]
public partial class ChaseThePig : CommandGroup
{
    public const string GameName = "Chase the Pig";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly State _state;
    private readonly ILogger<ChaseThePig> _logger;

    public ChaseThePig(InteractionContext context, State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestUserAPI userApi,
        ILogger<ChaseThePig> logger)
    {
        _context = context;
        _interactionApi = interactionApi;
        _guildApi = guildApi;
        _channelApi = channelApi;
        _state = state;
        _logger = logger;
        _userApi = userApi;
    }
    
    [Command("join")]
    [Description("Request to be enqueued to the waiting list to match with other players.")]
    public async Task<IResult> Handle(
        [Description("The base bet. The reward will be the number of players multiplied by this.")]
        float baseBet = 1.0f,
        [Description("(Optional) The 2nd player you want to invite.")]
        IUser? player2 = null,
        [Description("(Optional) The 3rd player you want to invite.")]
        IUser? player3 = null,
        [Description("(Optional) The 4th player you want to invite.")]
        IUser? player4 = null
    ) => await Join(baseBet, player2, player3, player4);

    public override string ToString()
        => "chase_the_pig";
}