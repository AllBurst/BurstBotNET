using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.BlackJack;

#pragma warning disable CA2252
[Group("blackjack")]
[Description("Play a black jack-like game with other people.")]
public partial class BlackJack : CommandGroup
{
    public const string GameName = "Black Jack";
    
    private readonly InteractionContext _context;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly State _state;
    private readonly ILogger<BlackJack> _logger;

    public BlackJack(
        InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        IDiscordRestChannelAPI channelApi,
        State state,
        ILogger<BlackJack> logger)
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
        [Description("The base bet. Players have to at least own this amount of tips before they can play.")]
        float baseBet = 1.0f,
        [Description("(Optional) The 2nd player you want to invite.")] 
        IUser? player2 = null,
        [Description("(Optional) The 3rd player you want to invite.")]
        IUser? player3 = null,
        [Description("(Optional) The 4th player you want to invite.")]
        IUser? player4 = null,
        [Description("(Optional) The 5th player you want to invite.")]
        IUser? player5 = null,
        [Description("(Optional) The 6th player you want to invite.")]
        IUser? player6 = null) => await Join(baseBet, player2, player3, player4, player5, player6);

    public override string ToString()
    {
        return "blackjack";
    }
}