using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BurstBotNET.Handlers;

public partial class Handlers
{
#pragma warning disable CS1998
    public async Task HandleClientError(DiscordClient client, ClientErrorEventArgs e)
    {
        client.Logger.LogError("A client error occurred when handling event: {Event}.\nException: {Exception}",
            e.EventName, e.Exception.Message);
    }
    
    public async Task HandleSocketError(DiscordClient client, SocketErrorEventArgs e)
#pragma warning restore CS1998
    {
        client.Logger.LogError("A socket error occurred.\nException: {Exception}", e.Exception.Message);
    }
}