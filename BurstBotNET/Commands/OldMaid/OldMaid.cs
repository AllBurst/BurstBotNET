using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.OldMaid;

[Group("old_maid")]
public partial class OldMaid : CommandGroup
{
    public const string GameName = "Old Maid";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly State _state;
    private readonly ILogger<OldMaid> _logger;

    public OldMaid(InteractionContext context, State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestUserAPI userApi,
        ILogger<OldMaid> logger)
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
        [Description("The base bet. The reward will be the number of players multiplied by this. The default is 10.0.")]
        float baseBet = 10.0f,
        [Description("(Optional) The 2nd player you want to invite.")]
        IUser? player2 = null,
        [Description("(Optional) The 3rd player you want to invite.")]
        IUser? player3 = null,
        [Description("(Optional) The 4th player you want to invite.")]
        IUser? player4 = null
    ) => await Join(baseBet, player2, player3, player4);

    public override string ToString()
        => "old_maid";
}