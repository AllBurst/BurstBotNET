using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;

namespace BurstBotNET.Commands.Trade;

[Group("trade")]
public partial class Trade : CommandGroup
{
    private readonly InteractionContext _context;
    private readonly State _state;
    private readonly IDiscordRestInteractionAPI _interactionApi;
    private readonly ILogger<Trade> _logger;
    private readonly IDiscordRestGuildAPI _guildApi;

    public Trade(
        InteractionContext context,
        State state,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        ILogger<Trade> logger)
    {
        _context = context;
        _state = state;
        _interactionApi = interactionApi;
        _logger = logger;
        _guildApi = guildApi;
    }
}