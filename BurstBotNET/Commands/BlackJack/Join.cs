using System.Collections.Immutable;
using System.Net;
using BurstBotNET.Api;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Data.Serializables;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Game.BlackJack;
using BurstBotNET.Shared.Models.Game.BlackJack.Serializables;
using BurstBotNET.Shared.Models.Game.Serializables;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Utilities = BurstBotNET.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.BlackJack;

public partial class BlackJack
{
    private async Task Join(DiscordClient client, InteractionCreateEventArgs e, Config config, GameStates gameStates, BurstApi burstApi, Localizations localizations)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var mentionedPlayers = e.Interaction.Data.Options.ToImmutableList()[0]
            .Options
            .Select(opt => (ulong)opt.Value)
            .ToList();
        var invoker = e.Interaction.User;
        mentionedPlayers.Add(invoker.Id);
        
        // Try getting all players' tips and check if they have enough tips.
        RawTip? invokerTip = null;
        var getTipTasks = mentionedPlayers
            .Select(async p =>
            {
                var response = await burstApi.SendRawRequest<object>($"/tip/{p}", ApiRequestType.Get, null);

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

        var joinRequest = new BlackJackJoinRequest
        {
            ClientType = ClientType.Discord,
            PlayerIds = mentionedPlayers
        };
        var joinGameResponse = await burstApi.SendRawRequest("/black_jack/join", ApiRequestType.Post, joinRequest);
        var responseCode = joinGameResponse.ResponseMessage.StatusCode;

        var playerCount = mentionedPlayers.Count;
        var unit = playerCount == 1 ? "player" : "players";
        var joinStatus = new BlackJackJoinStatus();

        var reply = new DiscordWebhookBuilder()
            .WithContent(responseCode switch
            {
                HttpStatusCode.BadRequest =>
                    "Sorry! It seems that at least one of the players you mentioned has already joined the waiting list!",
                HttpStatusCode.NotFound =>
                    "Sorry, but all players who want to join the game have to join the server first!",
                HttpStatusCode.InternalServerError =>
                    $"Sorry, but an unknown error occurred! Could you report this to the developers: **{responseCode}: {joinGameResponse.ResponseMessage.ReasonPhrase}**",
                _ => HandleSuccessfulJoinStatus(joinGameResponse, unit, ref joinStatus)
            });
        
        if (joinStatus == null)
        {
            await e.Interaction.EditOriginalResponseAsync(reply);
            return;
        }

        var invokingMember = await e.Interaction.Guild.GetMemberAsync(invoker.Id);
        var botUser = client.CurrentUser;

        switch (joinStatus.StatusType)
        {
            case BlackJackJoinStatusType.Waiting:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await burstApi.WaitForGame(this, joinStatus, e, invokingMember, botUser, "", gameStates, config,
                            localizations, client.Logger);
                    }
                    catch (Exception ex)
                    {
                        client.Logger.LogError("WebSocket failed: {Exception}", ex.Message);
                    }
                });
                break;
            }
            case BlackJackJoinStatusType.Start:
            {
                reply = reply.AddEmbed(Utilities.BuildBlackJackEmbed(invokingMember, botUser, joinStatus, "", null));
                var message = await e.Interaction.EditOriginalResponseAsync(reply);
                await HandleStartGameReactions(e, message, invokingMember, botUser, joinStatus, mentionedPlayers,
                    gameStates,
                    burstApi, localizations, config, client.Logger);
                break;
            }
            case BlackJackJoinStatusType.Matched:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildBlackJackEmbed(invokingMember, botUser, joinStatus, "", null)));
                var guild = e.Interaction.Guild;
                var textChannel = await burstApi.CreatePlayerChannel(guild, invokingMember);
                await AddBlackJackPlayerState(
                    joinStatus.GameId ?? "",
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
                    }, gameStates
                );
                _ = Task.Run(() =>
                    StartListening(joinStatus.GameId ?? "", config, gameStates, guild, localizations, client.Logger));
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported join status type.");
        }
    }

    private async Task HandleStartGameReactions(
        InteractionCreateEventArgs e,
        DiscordMessage originalMessage,
        DiscordMember invokingMember,
        DiscordUser botUser,
        BlackJackJoinStatus joinStatus,
        IEnumerable<ulong> playerIds,
        GameStates gameStates,
        BurstApi burstApi,
        Localizations localizations,
        Config config,
        ILogger logger)
    {
        await originalMessage.CreateReactionAsync(Constants.CheckMarkEmoji);
        await originalMessage.CreateReactionAsync(Constants.CrossMarkEmoji);
        await originalMessage.CreateReactionAsync(Constants.PlayMarkEmoji);
        var secondsRemained = 30;
        var cancelled = false;
        var fastStarted = false;
        var confirmedUsers = new List<DiscordUser>();

        while (secondsRemained > 0)
        {
            var reactions = await originalMessage.CollectReactionsAsync(TimeSpan.FromSeconds(5));
            foreach (var reaction in reactions)
            {
                if (reaction.Emoji.Equals(Constants.CheckMarkEmoji))
                {
                    confirmedUsers = reaction.Users
                        .Where(u => !u.IsBot && playerIds.Contains(u.Id))
                        .ToList();
                }
                else if (reaction.Emoji.Equals(Constants.CrossMarkEmoji))
                {
                    var cancelledUsers = reaction.Users
                        .Where(u => !u.IsBot)
                        .Select(u => u.Id)
                        .ToImmutableList();
                    if (!cancelledUsers.Contains(invokingMember.Id)) continue;
                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("❌ Cancelled."));
                    cancelled = true;
                }
                else if (reaction.Emoji.Equals(Constants.PlayMarkEmoji))
                {
                    var fastStartUsers = reaction
                        .Users
                        .Where(u => !u.IsBot)
                        .Select(u => u.Id)
                        .ToImmutableList();
                    fastStarted = fastStartUsers.Contains(invokingMember.Id);
                }
            }
            
            if (cancelled || fastStarted)
                break;

            secondsRemained -= 5;
            var confirmedUsersString =
                $"\nConfirmed players: \n{string.Join('\n', confirmedUsers.Select(u => $"💠<@!{u.Id}>"))}";
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .AddEmbed(Utilities.BuildBlackJackEmbed(invokingMember, botUser, joinStatus, confirmedUsersString,
                    secondsRemained)));
        }

        if (cancelled)
            return;

        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
            .WithContent(
                "<:burst_spade:910826637657010226> <:burst_heart:910826529511051284> **GAME STARTED!** <:burst_diamond:910826609576140821> <:burst_club:910826578336948234>"));

        var guild = e.Interaction.Guild;
        var members = await Task.WhenAll(confirmedUsers
            .Select(async u => await guild.GetMemberAsync(u.Id)));
        var matchData = await burstApi
            .SendRawRequest("/black_jack/join/confirm", ApiRequestType.Post, new BlackJackJoinStatus
            {
                StatusType = BlackJackJoinStatusType.Start,
                PlayerIds = members.Select(m => m.Id).ToList()
            })
            .ReceiveJson<BlackJackJoinStatus>();

        foreach (var member in members)
        {
            var playerTip = await burstApi
                .SendRawRequest<object>($"/tip/{member.Id}", ApiRequestType.Get, null)
                .ReceiveJson<RawTip>();
            var textChannel = await burstApi.CreatePlayerChannel(guild, member);
            await AddBlackJackPlayerState(matchData.GameId ?? "", new BlackJackPlayerState
            {
                AvatarUrl = member.AvatarUrl,
                BetTips = Constants.StartingBet,
                GameId = matchData.GameId ?? "",
                PlayerId = member.Id,
                PlayerName = member.DisplayName,
                TextChannel = textChannel,
                OwnTips = playerTip.Amount,
                Order = 0
            }, gameStates);
            _ = Task.Run(() =>
                StartListening(matchData.GameId ?? "", config, gameStates, guild, localizations, logger));
        }
    }

    private static string HandleSuccessfulJoinStatus(IFlurlResponse response, string unit, ref BlackJackJoinStatus joinStatus)
    {
        var newJoinStatus = response.GetJsonAsync<BlackJackJoinStatus>().GetAwaiter().GetResult();
        joinStatus = joinStatus! with
        {
            StatusType = newJoinStatus.StatusType,
            SocketIdentifier = newJoinStatus.SocketIdentifier,
            GameId = newJoinStatus.GameId,
            PlayerIds = newJoinStatus.PlayerIds
        };
        return newJoinStatus.StatusType switch
        {
            BlackJackJoinStatusType.Waiting =>
                $"Successfully started a game with $playerCount initial {unit}! Please wait for matching...",
            BlackJackJoinStatusType.Start => $"Successfully started a game with $playerCount initial {unit}!",
            BlackJackJoinStatusType.Matched =>
                $"Successfully matched a game with {joinStatus.PlayerIds.Count} players! Preparing the game...",
            _ => throw new InvalidOperationException("Incorrect join status found.")
        };
    }
}