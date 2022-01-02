using BurstBotNET.Commands.BlackJack;
using BurstBotShared.Shared.Models.Data;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace BurstBotNET.Handlers;

#pragma warning disable CA2252
public class MessageResponder : IResponder<IMessageCreate>
{
    private readonly State _state;
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly ILogger<MessageResponder> _logger;

    public MessageResponder(State state, IDiscordRestChannelAPI channelApi, ILogger<MessageResponder> logger)
    {
        _state = state;
        _channelApi = channelApi;
        _logger = logger;
    }
    
    public Task<Result> RespondAsync(IMessageCreate gatewayEvent, CancellationToken ct = new())
    {
        var isBotDefined = gatewayEvent.Author.IsBot.IsDefined(out var isBot);
        if (!isBotDefined || isBot) return Task.FromResult(Result.FromSuccess());

        var guildDefined = gatewayEvent.GuildID.IsDefined(out var guild);
        if (!guildDefined) return Task.FromResult(Result.FromSuccess());

        if (_state.GameStates.BlackJackGameStates.Item2.Contains(gatewayEvent.ChannelID))
        {
            _ = Task.Run(async () =>
            {
                await BlackJack.HandleBlackJackMessage(gatewayEvent, _state.GameStates,
                    gatewayEvent.ChannelID, _state.Localizations, _channelApi, _logger);
            }, ct);
        }
        
        return Task.FromResult(Result.FromSuccess());
    }
}