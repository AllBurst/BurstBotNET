using System.ComponentModel;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands.Rewards;

public class Weekly : CommandGroup
{
    private readonly InteractionContext _context;
    private readonly IDiscordRestUserAPI _userApi;
    private readonly ILogger<Weekly> _logger;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly State _state;
    
    public Weekly(
        InteractionContext context,
        IDiscordRestUserAPI userApi,
        IDiscordRestInteractionAPI interactionApi,
        State state,
        ILogger<Weekly> logger)
    {
        _context = context;
        _userApi = userApi;
        _logger = logger;
        _interactionApi = interactionApi;
        _state = state;
    }

    [Command("weekly")]
    [Description("Get your weekly reward of 700 tips here.")]
    public async Task<IResult> Handle()
    {
        var rewardResult =
            await Rewards.GetReward(_context, _userApi, _interactionApi, PlayerRewardType.Weekly, _state, _logger);
        return rewardResult.IsSuccess ? Result.FromSuccess() : Result.FromError(rewardResult.Error);
    }

    public override string ToString() => "weekly";
}