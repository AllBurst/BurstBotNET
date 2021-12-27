using System.Collections.Immutable;
using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.BlackJack;

#pragma warning disable CA2252
public partial class BlackJack
{
    private async Task Join(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var mentionedPlayers = new List<ulong>();
        var options = e.Interaction.Data.Options.ToImmutableArray();
        if (options[0].Options != null && options[0].Options.Any())
            mentionedPlayers.AddRange(options[0]
                .Options
                .Select(opt => (ulong)opt.Value));
        var invoker = e.Interaction.User;
        mentionedPlayers.Add(invoker.Id);

        // Try getting all players' tips and check if they have enough tips.
        RawTip? invokerTip = null;
        var getTipTasks = mentionedPlayers
            .Select(async p =>
            {
                var response = await state.BurstApi.SendRawRequest<object>($"/tip/{p}", ApiRequestType.Get, null);

                if (!response.ResponseMessage.IsSuccessStatusCode)
                    return null;

                var playerTip = await response.GetJsonAsync<RawTip>();

                if (invoker.Id == p)
                    invokerTip = playerTip;

                return playerTip is { Amount: < 1 } ? null : playerTip;
            });

        var hasInvalidPlayers = (await Task.WhenAll(getTipTasks))
            .Any(tip => tip == null);

        if (hasInvalidPlayers)
        {
            await e.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder()
                    .WithContent(
                        "Sorry, but either one of the players you invited hasn't joined the server yet, or he doesn't have enough tips to join a game!"));
            return;
        }

        var joinRequest = new GenericJoinRequest
        {
            ClientType = ClientType.Discord,
            GameType = GameType.BlackJack,
            PlayerIds = mentionedPlayers
        };
        var joinGameResponse =
            await state.BurstApi.SendRawRequest("/black_jack/join", ApiRequestType.Post, joinRequest);

        var playerCount = mentionedPlayers.Count;
        var unit = playerCount == 1 ? "player" : "players";

        var (joinStatus, reply) = BurstApi.HandleMatchGameHttpStatuses(joinGameResponse, unit, GameType.BlackJack);

        if (joinStatus == null)
        {
            await e.Interaction.EditOriginalResponseAsync(reply);
            return;
        }

        var invokingMember = await e.Interaction.Guild.GetMemberAsync(invoker.Id);
        var botUser = client.CurrentUser;

        switch (joinStatus.StatusType)
        {
            case GenericJoinStatusType.Waiting:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await state.BurstApi.WaitForBlackJackGame(joinStatus, e, invokingMember,
                            botUser, "",
                            client.Logger);
                        if (!waitingResult.HasValue)
                            throw new Exception("Failed to get waiting result for Black Jack.");

                        var (matchData, playerState) = waitingResult.Value;
                        await AddBlackJackPlayerState(matchData.GameId ?? "", e.Interaction.Guild, playerState,
                            state.GameStates);
                        _ = Task.Run(() =>
                            StartListening(matchData.GameId ?? "",
                                state.Config,
                                state.GameStates,
                                state.DeckService,
                                state.Localizations, client.Logger));
                    }
                    catch (Exception ex)
                    {
                        client.Logger.LogError("WebSocket failed: {Exception}", ex);
                    }
                });
                break;
            }
            case GenericJoinStatusType.Start:
            {
                reply = reply.AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName, "",
                    null));
                var message = await e.Interaction.EditOriginalResponseAsync(reply);
                var reactionResult = await BurstApi.HandleStartGameReactions(GameName, e, message, invokingMember,
                    botUser, joinStatus,
                    mentionedPlayers,
                    "/black_jack/join/confirm", state, client.Logger);
                if (!reactionResult.HasValue)
                {
                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                        .WithContent(ErrorMessages.HandleReactionFailed));
                    return;
                }

                var (members, matchData) = reactionResult.Value;
                var guild = e.Interaction.Guild;
                foreach (var member in members)
                {
                    var playerTip = await state.BurstApi
                        .SendRawRequest<object>($"/tip/{member.Id}", ApiRequestType.Get, null)
                        .ReceiveJson<RawTip>();
                    var textChannel = await state.BurstApi.CreatePlayerChannel(guild, member);
                    await AddBlackJackPlayerState(matchData.GameId ?? "", guild, new BlackJackPlayerState
                    {
                        AvatarUrl = member.AvatarUrl,
                        BetTips = Constants.StartingBet,
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.Id,
                        PlayerName = member.DisplayName,
                        TextChannel = textChannel,
                        OwnTips = playerTip.Amount,
                        Order = 0
                    }, state.GameStates);
                    _ = Task.Run(() =>
                        StartListening(matchData.GameId ?? "", state.Config, state.GameStates,
                            state.DeckService,
                            state.Localizations, client.Logger));
                }

                break;
            }
            case GenericJoinStatusType.Matched:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName, "", null)));
                var guild = e.Interaction.Guild;
                var textChannel = await state.BurstApi.CreatePlayerChannel(guild, invokingMember);
                await AddBlackJackPlayerState(
                    joinStatus.GameId ?? "",
                    guild,
                    new BlackJackPlayerState
                    {
                        GameId = joinStatus.GameId ?? "",
                        PlayerId = invokingMember.Id,
                        PlayerName = invokingMember.DisplayName,
                        TextChannel = textChannel,
                        OwnTips = invokerTip?.Amount ?? 0,
                        BetTips = Constants.StartingBet,
                        Order = 0,
                        AvatarUrl = invokingMember.GetAvatarUrl(ImageFormat.Auto)
                    }, state.GameStates
                );
                _ = Task.Run(() =>
                    StartListening(joinStatus.GameId ?? "", state.Config, state.GameStates,
                        state.DeckService,
                        state.Localizations, client.Logger));
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported join status type.");
        }
    }
}