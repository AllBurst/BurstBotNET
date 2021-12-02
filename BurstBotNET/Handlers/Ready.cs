using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace BurstBotNET.Handlers;

public partial class Handlers
{
#pragma warning disable CS1998
    public static async Task HandleReady(DiscordClient client, ReadyEventArgs e)
#pragma warning restore CS1998
    {
        client.Logger.LogInformation("Successfully connected to the gateway");
        client.Logger.LogInformation("{Name}#{Discriminator} is now online", client.CurrentUser.Username,
            client.CurrentUser.Discriminator);
    }
}