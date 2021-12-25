using System.Collections.Immutable;
using System.Net;
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

public partial class BlackJack
{
    private async Task Join(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var mentionedPlayers = new List<ulong>();
        var options = e.Interaction.Data.Options.ToImmutableList();
        if (options[0].Options != null && options[0].Options.Any())
        {
            mentionedPlayers.AddRange(options[0]
                .Options
                .Select(opt => (ulong)opt.Value));
        }
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
        var joinGameResponse = await state.BurstApi.SendRawRequest("/black_jack/join", ApiRequestType.Post, joinRequest);
        var responseCode = joinGameResponse.ResponseMessage.StatusCode;

        var playerCount = mentionedPlayers.Count;
        var unit = playerCount == 1 ? "player" : "players";
        GenericJoinStatus? joinStatus = null;

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
            case GenericJoinStatusType.Waiting:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await state.BurstApi.WaitForBlackJackGame(joinStatus, e, invokingMember, botUser, "",
                            client.Logger);
                        if (!waitingResult.HasValue)
                            throw new Exception("Failed to get waiting result for Black Jack.");

                        var (matchData, playerState) = waitingResult.Value;
                        await AddBlackJackPlayerState(matchData.GameId ?? "", e.Interaction.Guild, playerState, state.GameStates);
                        _ = Task.Run(() =>
                            StartListening(matchData.GameId ?? "",
                                state.Config,
                                state.GameStates,
                                state.DeckService,
                                state.Localizations, client.Logger));
                    }
                    catch (Exception ex)
                    {
                        client.Logger.LogError("WebSocket failed: {Exception}", ex.Message);
                    }
                });
                break;
            }
            case GenericJoinStatusType.Start:
            {
                reply = reply.AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, "Black Jack", "", null));
                var message = await e.Interaction.EditOriginalResponseAsync(reply);
                await HandleStartGameReactions(e, message, invokingMember, botUser, joinStatus, mentionedPlayers,
                    state, client.Logger);
                break;
            }
            case GenericJoinStatusType.Matched:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, "Black Jack", "", null)));
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

    private static async Task HandleStartGameReactions(
        InteractionCreateEventArgs e,
        DiscordMessage originalMessage,
        DiscordMember invokingMember,
        DiscordUser botUser,
        GenericJoinStatus joinStatus,
        IEnumerable<ulong> playerIds,
        State state,
        ILogger logger)
    {
        await originalMessage.CreateReactionAsync(Constants.CheckMarkEmoji);
        await originalMessage.CreateReactionAsync(Constants.CrossMarkEmoji);
        await originalMessage.CreateReactionAsync(Constants.PlayMarkEmoji);
        var secondsRemained = 30;
        var cancelled = false;
        var confirmedUsers = new List<DiscordUser>();

        while (secondsRemained > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            
            confirmedUsers = (await originalMessage
                .GetReactionsAsync(Constants.CheckMarkEmoji))
                .Where(u => !u.IsBot && playerIds.Contains(u.Id))
                .ToList();

            var cancelledUsers = (await originalMessage
                    .GetReactionsAsync(Constants.CrossMarkEmoji))
                .Where(u => !u.IsBot)
                .Select(u => u.Id)
                .ToImmutableList();
            if (cancelledUsers.Contains(invokingMember.Id))
            {
                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("❌ Cancelled."));
                cancelled = true;
                break;
            }

            var fastStartUsers = (await originalMessage
                    .GetReactionsAsync(Constants.PlayMarkEmoji))
                .Where(u => !u.IsBot)
                .Select(u => u.Id)
                .ToImmutableList();
            if (fastStartUsers.Contains(invokingMember.Id))
                break;

            secondsRemained -= 5;
            var confirmedUsersString =
                $"\nConfirmed players: \n{string.Join('\n', confirmedUsers.Select(u => $"💠<@!{u.Id}>"))}";
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, "Black Jack", confirmedUsersString,
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
        var matchData = await state.BurstApi
            .SendRawRequest("/black_jack/join/confirm", ApiRequestType.Post, new GenericJoinStatus
            {
                StatusType = GenericJoinStatusType.Start,
                PlayerIds = members.Select(m => m.Id).ToList()
            })
            .ReceiveJson<GenericJoinStatus>();

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
                    state.Localizations, logger));
        }
    }

    // ReSharper disable once RedundantAssignment
    private static string HandleSuccessfulJoinStatus(IFlurlResponse response, string unit, ref GenericJoinStatus? joinStatus)
    {
        var newJoinStatus = response.GetJsonAsync<GenericJoinStatus>().GetAwaiter().GetResult();
        joinStatus = new GenericJoinStatus
        {
            StatusType = newJoinStatus.StatusType,
            SocketIdentifier = newJoinStatus.SocketIdentifier,
            GameId = newJoinStatus.GameId,
            PlayerIds = newJoinStatus.PlayerIds
        };
        return newJoinStatus.StatusType switch
        {
            GenericJoinStatusType.Waiting =>
                $"Successfully started a game with {joinStatus.PlayerIds.Count} initial {unit}! Please wait for matching...",
            GenericJoinStatusType.Start => $"Successfully started a game with {joinStatus.PlayerIds.Count} initial {unit}!",
            GenericJoinStatusType.Matched =>
                $"Successfully matched a game with {joinStatus.PlayerIds.Count} players! Preparing the game...",
            _ => throw new InvalidOperationException("Incorrect join status found.")
        };
    }
}