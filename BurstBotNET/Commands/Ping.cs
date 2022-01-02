using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotNET.Commands;

public class Ping : CommandGroup
{
    private readonly InteractionContext _context;
    private readonly ILogger<Ping> _logger;
    private readonly IDiscordRestInteractionAPI _interactionApi;

    public Ping(InteractionContext context,
        ILogger<Ping> logger,
        IDiscordRestInteractionAPI interactionApi)
    {
        _context = context;
        _logger = logger;
        _interactionApi = interactionApi;
    }

    [Command("ping")]
    [Description("Returns the latency between the bot and Discord API.")]
    public async Task<IResult> Handle()
    {
        var startTime = DateTime.Now;
        var result = await _interactionApi.EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
            "Pinging...");
        var latency = DateTime.Now - startTime;
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to respond to slash command: {Reason}, inner: {Detail}", 
                result.Error.Message, result.Inner);
            return Result.FromError(result.Error);
        }
        
        var sentMessage = await _interactionApi
            .GetOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token);
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to get the original response to slash command: {Reason}, inner: {Detail}", sentMessage.Error?.Message, result.Inner);
            return Result.FromError(sentMessage);
        }

        var editResult = await _interactionApi
            .EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
                $"Pong!!\nLatency is {latency.Milliseconds} ms");
        return editResult.IsSuccess ? Result.FromError(editResult) : Result.FromSuccess();
    }

    public override string ToString() => "ping";
}