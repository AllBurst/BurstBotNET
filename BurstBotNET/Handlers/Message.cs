using BurstBotNET.Commands.BlackJack;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Handlers;

public partial class Handlers
{
    public async Task HandleMessage(DiscordClient client, MessageCreateEventArgs e)
    {
        if (e.Author.IsBot)
            return;
        
        if (_state.GameStates.BlackJackGameStates.Item2.Contains(e.Channel.Id))
            await BlackJack.HandleBlackJackMessage(
                client,
                e,
                _state.GameStates,
                e.Channel.Id,
                _state.Localizations
            );
    }
}