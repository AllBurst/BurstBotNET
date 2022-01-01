using BurstBotNET.Commands.BlackJack;
using BurstBotNET.Commands.ChinesePoker;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace BurstBotNET.Handlers;

#pragma warning disable CA2252
public partial class Handlers
{
    /*public Task HandleMessage(DiscordClient client, MessageCreateEventArgs e)
    {
        if (e.Author.IsBot || e.Guild == null)
            return Task.CompletedTask;

        if (_state.GameStates.BlackJackGameStates.Item2.Contains(e.Channel.Id))
            _ = Task.Run(async () => await BlackJack.HandleBlackJackMessage(
                client,
                e,
                _state.GameStates,
                e.Channel.Id,
                _state.Localizations
            ));

        return Task.CompletedTask;
    }*/
}