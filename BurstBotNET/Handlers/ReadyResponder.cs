using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace BurstBotNET.Handlers;

#pragma warning disable CS1998
public class ReadyResponder : IResponder<IReady>
{
    private readonly ILogger<ReadyResponder> _logger;

    public ReadyResponder(ILogger<ReadyResponder> logger, IDiscordRestApplicationAPI applicationApi)
    {
        _logger = logger;
        
    }

    public Task<Result> RespondAsync(IReady gatewayEvent, CancellationToken ct = default)
    {
        _logger.LogInformation("Successfully connected to the gateway");
        _logger.LogInformation("{Name}#{Discriminator} is now online", gatewayEvent.User.Username,
            gatewayEvent.User.Discriminator);
        return Task.FromResult(Result.FromSuccess());
    }
}