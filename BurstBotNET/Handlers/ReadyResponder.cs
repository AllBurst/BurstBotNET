using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace BurstBotNET.Handlers;

#pragma warning disable CS1998
public class ReadyResponder : IResponder<IReady>
{
    private readonly ILogger<ReadyResponder> _logger;
    private readonly DiscordGatewayClient _gatewayClient;

    public ReadyResponder(ILogger<ReadyResponder> logger, DiscordGatewayClient gatewayClient)
    {
        _logger = logger;
        _gatewayClient = gatewayClient;
    }

    public Task<Result> RespondAsync(IReady gatewayEvent, CancellationToken ct = default)
    {
        _logger.LogInformation("Successfully connected to the gateway");
        _logger.LogInformation("{Name}#{Discriminator} is now online", gatewayEvent.User.Username,
            gatewayEvent.User.Discriminator);

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromHours(1), ct);
                if (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Activity update task has been cancelled");
                    break;
                }
                
                var newActivity = new UpdatePresence(ClientStatus.Online, false, null,
                    new[] { Constants.Activities.Choose() });
                _gatewayClient.SubmitCommand(newActivity);
            }
        }, ct);
        
        return Task.FromResult(Result.FromSuccess());
    }
}