using BurstBotShared.Api;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotNET.Commands.ChinesePoker;

#pragma warning disable CA2252
public partial class ChinesePoker
{
    private async Task Join(DiscordClient client, InteractionCreateEventArgs e, State state)
    {
        await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var mentionedPlayers = new List<ulong> { e.Interaction.User.Id };
        var options = e.Interaction.Data.Options.ElementAt(0).Options.ToArray();
        var baseBet = (float)(double)options[0].Value;

        if (options.Length > 1)
        {
            var remainingOptions = options[1..]
                .Select(opt => (ulong)opt.Value);
            mentionedPlayers.AddRange(remainingOptions);
        }
        
        var invoker = e.Interaction.User;

        var getTipTasks = mentionedPlayers
            .Select(async p =>
            {
                var response = await state.BurstApi.SendRawRequest<object>($"/tip/{p}", ApiRequestType.Get, null);
                if (!response.ResponseMessage.IsSuccessStatusCode)
                    return null;

                var playerTip = await response.GetJsonAsync<RawTip>();
                
                return playerTip.Amount < baseBet ? null : playerTip;
            });

        var playerTips = await Task.WhenAll(getTipTasks);
        var hasInvalidPlayer = playerTips.Any(tip => tip == null);

        if (hasInvalidPlayer)
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
                .WithContent(ErrorMessages.InvalidPlayer));
            return;
        }

        var joinRequest = new GenericJoinRequest
        {
            ClientType = ClientType.Discord,
            GameType = GameType.ChinesePoker,
            PlayerIds = mentionedPlayers
        };
        var joinResponse = await state.BurstApi.SendRawRequest("/chinese_poker/join", ApiRequestType.Post, joinRequest);
        var playerCount = mentionedPlayers.Count;
        var unit = playerCount > 1 ? "players" : "player";

        var (joinStatus, reply) = BurstApi.HandleMatchGameHttpStatuses(joinResponse, unit, GameType.ChinesePoker);
        if (joinStatus == null)
        {
            await e.Interaction.EditOriginalResponseAsync(reply);
            return;
        }

        var invokingMember = await e.Interaction.Guild.GetMemberAsync(invoker.Id);
        var botUser = client.CurrentUser;

        switch (joinStatus.StatusType)
        {
            case GenericJoinStatusType.Start:
            {
                reply = reply.AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName,
                    "", null));
                var message = await e.Interaction.EditOriginalResponseAsync(reply);
                var reactionResult = await BurstApi.HandleStartGameReactions(GameName, e, message,
                    invokingMember, botUser, joinStatus, mentionedPlayers, "/chinese_poker/join/confirm", state,
                    client.Logger, 4);

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
                    var textChannel = await state.BurstApi.CreatePlayerChannel(guild, member);
                    await AddChinesePokerPlayerState(matchData.GameId ?? "", guild, new ChinesePokerPlayerState
                    {
                        AvatarUrl = member.GetAvatarUrl(ImageFormat.Auto),
                        GameId = matchData.GameId ?? "",
                        PlayerId = member.Id,
                        PlayerName = member.DisplayName,
                        TextChannel = textChannel,
                        Member = member
                    }, state.GameStates, baseBet);
                    _ = Task.Run(() => StartListening(matchData.GameId ?? "", state, client.Logger));
                }
                
                break;
            }
            case GenericJoinStatusType.Matched:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, GameName, "",
                        null)));
                var guild = e.Interaction.Guild;
                var textChannel = await state.BurstApi.CreatePlayerChannel(guild, invokingMember);
                await AddChinesePokerPlayerState(joinStatus.GameId ?? "", guild, new ChinesePokerPlayerState
                {
                    AvatarUrl = invokingMember.GetAvatarUrl(ImageFormat.Auto),
                    GameId = joinStatus.GameId ?? "",
                    PlayerId = invokingMember.Id,
                    PlayerName = invokingMember.DisplayName,
                    TextChannel = textChannel,
                    Member = invokingMember
                }, state.GameStates, baseBet);
                _ = Task.Run(() => StartListening(joinStatus.GameId ?? "", state, client.Logger));
                break;
            }
            case GenericJoinStatusType.Waiting:
            {
                await e.Interaction.EditOriginalResponseAsync(reply);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var waitingResult = await state.BurstApi.WaitForChinesePokerGame(joinStatus, e, invokingMember,
                            botUser, "", client.Logger);
                        if (!waitingResult.HasValue)
                            throw new Exception($"Failed to get waiting result for {GameName}.");

                        var (matchData, playerState) = waitingResult.Value;
                        await AddChinesePokerPlayerState(matchData.GameId ?? "", e.Interaction.Guild, playerState,
                            state.GameStates, baseBet);
                        _ = Task.Run(() => StartListening(matchData.GameId ?? "", state, client.Logger));
                    }
                    catch (Exception ex)
                    {
                        client.Logger.LogError("WebSocket failed: {Exception}", ex);
                    }
                });
                break;
            }
        }
    }
}