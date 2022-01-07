using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.ChinesePoker;

#pragma warning disable CA2252
[Group("chinese_poker")]
public partial class ChinesePoker : Remora.Commands.Groups.CommandGroup
{
    public const string GameName = "Chinese Poker";

    private readonly InteractionContext _context;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly State _state;
    private readonly ILogger<ChinesePoker> _logger;
    
    public ChinesePoker(
        InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        State state,
        ILogger<ChinesePoker> logger)
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
        [Description("The requested base bet. Each player's final reward will be units won/lost multiplied by this.")]
        float baseBet = 1.0f,
        [Description("(Optional) The 2nd player you want to invite.")] 
        IUser? player2 = null,
        [Description("(Optional) The 3rd player you want to invite.")]
        IUser? player3 = null,
        [Description("(Optional) The 4th player you want to invite.")]
        IUser? player4 = null) => await Join(baseBet, player2, player3, player4);

    public override string ToString()
    {
        return "chinese_poker";
    }
}