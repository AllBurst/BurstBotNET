using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BurstBotShared.Services;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.Serializables;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Rest.Core;
using ChannelType = Remora.Discord.API.Abstractions.Objects.ChannelType;
using Constants = BurstBotShared.Shared.Constants;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotShared.Api;

public sealed class BurstApi
{
    private const string CategoryName = "All-Burst-Category";
    private const int BufferSize = 2048;

    private static readonly DiscordPermissionSet PlayerPermissions = new(
        DiscordPermission.AddReactions,
        DiscordPermission.EmbedLinks,
        DiscordPermission.ReadMessageHistory,
        DiscordPermission.SendMessages,
        DiscordPermission.UseExternalEmojis,
        DiscordPermission.ViewChannel);

    private static readonly DiscordPermissionSet BotPermissions = new(
        DiscordPermission.AddReactions,
        DiscordPermission.EmbedLinks,
        DiscordPermission.ReadMessageHistory,
        DiscordPermission.SendMessages,
        DiscordPermission.UseExternalEmojis,
        DiscordPermission.ViewChannel,
        DiscordPermission.ManageChannels,
        DiscordPermission.ManageMessages);

    private readonly ConcurrentDictionary<ulong, List<IChannel>> _guildChannelList = new();
    private readonly string _serverEndpoint;
    private readonly string _socketEndpoint;
    private readonly int _socketPort;

    public BurstApi(Config config)
    {
        _serverEndpoint = config.ServerEndpoint;
        _socketEndpoint = config.SocketEndpoint;
        _socketPort = config.SocketPort;
    }

    public async Task<IFlurlResponse> SendRawRequest<TPayloadType>(string endpoint, ApiRequestType requestType,
        TPayloadType? payload)
    {
        var url = _serverEndpoint + endpoint;
        try
        {
            return requestType switch
            {
                ApiRequestType.Get => await url.GetAsync(),
                ApiRequestType.Post => await Post(url, payload),
                ApiRequestType.Patch => await Patch(url, payload),
                _ => throw new InvalidOperationException("Unsupported raw request type.")
            };
        }
        catch (FlurlHttpException ex)
        {
            return ex.Call.Response;
        }
    }

    public async Task<IFlurlResponse> GetReward(PlayerRewardType rewardType, ulong playerId)
    {
        var endpoint = rewardType switch
        {
            PlayerRewardType.Daily => $"{_serverEndpoint}/player/{playerId}/daily",
            PlayerRewardType.Weekly => $"{_serverEndpoint}/player/{playerId}/weekly",
            _ => throw new ArgumentException("Incorrect reward type.")
        };

        try
        {
            return await endpoint.GetAsync();
        }
        catch (FlurlHttpException ex)
        {
            return ex.Call.Response;
        }
    }

    public static async Task<IEnumerable<IGuildMember>> GetMembers(
        Snowflake guild,
        IEnumerable<Snowflake> mentionedPlayers,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        var getMembersTasks = mentionedPlayers
            .Select(async pId =>
            {
                var getMemberResult = await guildApi.GetGuildMemberAsync(guild, pId);
                if (getMemberResult.IsSuccess) return getMemberResult.Entity;
                
                logger.LogError("Failed to get guild member {MemberId}: {Reason}, inner: {Inner}",
                    pId, getMemberResult.Error.Message, getMemberResult.Inner);
                return null;
            });

        return (await Task.WhenAll(getMembersTasks))
            .Where(m => m != null)
            .Select(m => m!);
    }

    private static async Task<GenericJoinStatus?> GenericWaitForGame(
        GenericJoinStatus waitingData,
        InteractionContext context,
        IEnumerable<IGuildMember> mentionedPlayers,
        IUser botUser,
        string gameName,
        string description,
        AmqpService amqpService,
        IDiscordRestInteractionAPI interactionApi,
        ILogger logger)
    {
        var serializedWaitingData = JsonSerializer.SerializeToUtf8Bytes(waitingData);
        await amqpService.RequestMatch(waitingData.GameType, serializedWaitingData);
        var matchData = await amqpService.ReceiveMatchData(waitingData.SocketIdentifier!);

        if (matchData == null) return null;
        
        var participatingPlayers = mentionedPlayers.ToImmutableArray();
        foreach (var player in participatingPlayers)
        {
            var followUpResult = await interactionApi
                .CreateFollowupMessageAsync(
                    context.ApplicationID,
                    context.Token,
                    embeds: new[]
                    {
                        Utilities.BuildGameEmbed(player, botUser, matchData, gameName, description,
                            null)
                    });

            if (!followUpResult.IsSuccess)
            {
                logger.LogError("Failed to create follow-up message: {Reason}, inner: {Inner}",
                    followUpResult.Error.Message, followUpResult.Inner);
            }
        }

        return matchData;
    }

    private async Task<GenericJoinStatus?> GenericWaitForGame(
        GenericJoinStatus waitingData,
        InteractionContext context,
        IEnumerable<IGuildMember> mentionedPlayers,
        IUser botUser,
        string gameName,
        string description,
        IDiscordRestInteractionAPI interactionApi,
        ILogger logger)
    {
        using var socketSession = new ClientWebSocket();
        socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var cancellationTokenSource = new CancellationTokenSource();
        var url = new Uri(_socketPort != 0 ? $"ws://{_socketEndpoint}:{_socketPort}" : $"wss://{_socketEndpoint}");
        await socketSession.ConnectAsync(url, cancellationTokenSource.Token);

        while (true)
            if (socketSession.State == WebSocketState.Open)
                break;

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(waitingData)),
            WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationTokenSource.Token);

        while (true)
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var receiveTask = ReceiveMatchData(socketSession, cancellationTokenSource.Token);
            var timeoutTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), timeoutCancellationTokenSource.Token);
                    return new GenericJoinStatus
                    {
                        SocketIdentifier = null,
                        GameId = null,
                        StatusType = GenericJoinStatusType.TimedOut
                    };
                }
                catch (TaskCanceledException)
                {
                }
                finally
                {
                    timeoutCancellationTokenSource.Dispose();
                }

                return null;
            }, cancellationTokenSource.Token);

            var matchData = await await Task.WhenAny(new[] { receiveTask, timeoutTask });
            if (matchData is { StatusType: GenericJoinStatusType.TimedOut })
            {
                const string message = "Timeout because no match game is found";
                logger.LogDebug(message);
                await socketSession.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, message,
                    cancellationTokenSource.Token);
                _ = Task.Run(() =>
                {
                    cancellationTokenSource.Cancel();
                    logger.LogDebug("All tasks for matching have been cancelled");
                    cancellationTokenSource.Dispose();
                });
                logger.LogDebug("WebSocket closed due to timeout");
                break;
            }

            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
                logger.LogDebug("Timeout task for matching has been cancelled");
            });

            if (matchData is not { StatusType: GenericJoinStatusType.Matched } || matchData.GameId == null) continue;

            await socketSession.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Matched.",
                cancellationTokenSource.Token);
            cancellationTokenSource.Dispose();

            var participatingPlayers = mentionedPlayers.ToImmutableArray();
            foreach (var player in participatingPlayers)
            {
                var followUpResult = await interactionApi
                    .CreateFollowupMessageAsync(
                        context.ApplicationID,
                        context.Token,
                        embeds: new[]
                        {
                            Utilities.BuildGameEmbed(player, botUser, matchData, gameName, description,
                                null)
                        });

                if (!followUpResult.IsSuccess)
                {
                    logger.LogError("Failed to create follow-up message: {Reason}, inner: {Inner}",
                        followUpResult.Error.Message, followUpResult.Inner);
                } 
            }
            
            return matchData;
        }

        return null;
    }

    public static (GenericJoinStatus?, string) HandleMatchGameHttpStatuses(IFlurlResponse response,
        string unit, GameType gameType)
    {
        var responseCode = response.ResponseMessage.StatusCode;
        return responseCode switch
        {
            HttpStatusCode.BadRequest =>
                (null,
                    "Sorry! It seems that at least one of the players you mentioned has already joined the waiting list!"),
            HttpStatusCode.NotFound =>
                (null,
                    "Sorry, but all players who want to join the game have to join the server first!"),
            HttpStatusCode.InternalServerError =>
                (null,
                    $"Sorry, but an unknown error occurred! Could you report this to the developers: **{responseCode}: {response.ResponseMessage.ReasonPhrase}**"),
            _ => HandleSuccessfulJoinStatus(response, unit, gameType)
        };
    }

    public static async Task<(ImmutableArray<IGuildMember>, GenericJoinStatus)?> HandleStartGameReactions(
        string gameName,
        InteractionContext context,
        IMessage originalMessage,
        IGuildMember invokingMember,
        IUser botUser,
        GenericJoinStatus joinStatus,
        IEnumerable<ulong> playerIds,
        string confirmationEndpoint,
        State state,
        IDiscordRestChannelAPI channelApi,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger,
        int minPlayerCount = 2)
    {
        await channelApi.CreateReactionAsync(originalMessage.ChannelID, originalMessage.ID, Constants.CheckMark);
        await channelApi.CreateReactionAsync(originalMessage.ChannelID, originalMessage.ID, Constants.CrossMark);
        await channelApi.CreateReactionAsync(originalMessage.ChannelID, originalMessage.ID, Constants.PlayMark);
        
        var secondsRemained = 30;
        var cancelled = false;
        var confirmedUsers = new ImmutableArray<IUser>();

        while (secondsRemained > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));

            var checkMarkResult = await channelApi
                .GetReactionsAsync(originalMessage.ChannelID, originalMessage.ID, Constants.CheckMark);

            if (!checkMarkResult.IsSuccess)
                logger.LogError("Failed to get reactions for message: {Reason}, inner: {Inner}",
                    checkMarkResult.Error, checkMarkResult.Inner);

            confirmedUsers = checkMarkResult
                .Entity
                .Where(u =>
                {
                    var _ = u.IsBot.IsDefined(out var bot);
                    return !bot && playerIds.Contains(u.ID.Value);
                })
                .ToImmutableArray();
            
            var crossMarkResult = await channelApi
                .GetReactionsAsync(originalMessage.ChannelID, originalMessage.ID, Constants.CrossMark);

            if (!crossMarkResult.IsSuccess)
                logger.LogError("Failed to get reactions for message: {Reason}, inner: {Inner}",
                    crossMarkResult.Error, crossMarkResult.Inner);

            var cancelledUsers = crossMarkResult
                .Entity
                .Where(u =>
                {
                    var _ = u.IsBot.IsDefined(out var bot);
                    return !bot;
                })
                .Select(u => u.ID.Value)
                .ToImmutableArray();
            if (cancelledUsers.Contains(invokingMember.User.Value.ID.Value))
            {
                var failureResult = await interactionApi
                    .EditOriginalInteractionResponseAsync(
                        context.ApplicationID,
                        context.Token,
                        "❌ Cancelled.");

                if (!failureResult.IsSuccess)
                {
                    logger.LogError("Failed to edit original response: {Reason}, inner: {Inner}",
                        failureResult.Error.Message, failureResult.Inner);
                    continue;
                }
                
                cancelled = true;
                break;
            }
            
            var playMarkResult = await channelApi
                .GetReactionsAsync(originalMessage.ChannelID, originalMessage.ID, Constants.PlayMark);

            if (!playMarkResult.IsSuccess)
                logger.LogError("Failed to get reactions for message: {Reason}, inner: {Inner}",
                    playMarkResult.Error, playMarkResult.Inner);

            var fastStartUsers = playMarkResult
                .Entity
                .Where(u =>
                {
                    var _ = u.IsBot.IsDefined(out var bot);
                    return !bot;
                })
                .Select(u => u.ID.Value)
                .ToImmutableArray();
            if (fastStartUsers.Contains(invokingMember.User.Value.ID.Value))
                break;

            secondsRemained -= 5;
            var confirmedUsersString =
                $"\nConfirmed players: \n{string.Join('\n', confirmedUsers.Select(u => $"💠<@!{u.ID.Value}>"))}";
            
            var editResult = await interactionApi
                .EditOriginalInteractionResponseAsync(
                    context.ApplicationID,
                    context.Token,
                    embeds: new[]
                    {
                        Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, gameName,
                            confirmedUsersString,
                            secondsRemained)
                    });
            if (!editResult.IsSuccess)
            {
                logger.LogError("Failed to edit original response: {Reason}, inner: {Inner}",
                    editResult.Error.Message, editResult.Inner);
            }
        }

        if (cancelled || confirmedUsers.Length < minPlayerCount)
            return null;

        var startMessageResult = await interactionApi
            .EditOriginalInteractionResponseAsync(
                context.ApplicationID,
                context.Token,
                Constants.GameStarted);

        if (!startMessageResult.IsSuccess)
        {
            logger.LogError("Failed to edit original response: {Reason}, inner: {Inner}",
                startMessageResult.Error.Message, startMessageResult.Inner);
            return null;
        }

        var guildResult = context.GuildID.IsDefined(out var guild);
        if (!guildResult)
        {
            var errorMessageResult = await interactionApi
                .EditOriginalInteractionResponseAsync(
                    context.ApplicationID,
                    context.Token,
                    ErrorMessages.JoinNotInGuild);
            if (!errorMessageResult.IsSuccess)
                logger.LogError("Failed to edit original response: {Reason}, inner: {Inner}",
                    errorMessageResult.Error.Message, errorMessageResult.Inner);

            return null;
        }
        
        var members = (await Task.WhenAll(confirmedUsers
            .Select(async u => await guildApi.GetGuildMemberAsync(guild, u.ID))))
            .Select(result => result.Entity)
            .ToImmutableArray();
        var matchData = await state.BurstApi
            .SendRawRequest(confirmationEndpoint, ApiRequestType.Post, new GenericJoinStatus
            {
                StatusType = GenericJoinStatusType.Start,
                PlayerIds = members.Select(m => m.User.Value.ID.Value).ToList(),
                BaseBet = joinStatus.BaseBet
            })
            .ReceiveJson<GenericJoinStatus>();

        return (members, matchData);
    }

    public async Task<(GenericJoinStatus, ImmutableArray<BlackJackPlayerState>)?> WaitForBlackJackGame(
        GenericJoinStatus waitingData,
        InteractionContext context,
        IEnumerable<Snowflake> mentionedPlayers,
        IUser botUser,
        string description,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        var guild = await Utilities.GetGuildFromContext(context, interactionApi, logger);
        if (!guild.HasValue)
            return null;

        var participatingPlayers =
            (await GetMembers(guild.Value, mentionedPlayers, guildApi, logger)).ToImmutableArray();
        
        var matchData =
            await GenericWaitForGame(waitingData, context, participatingPlayers, botUser, "Black Jack", description,
                interactionApi, logger);

        if (matchData == null)
        {
            var result = await interactionApi
                .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                    "Sorry, but looks like there are not enough players waiting for a match!");
            if (!result.IsSuccess)
                logger.LogError("Failed to reply with match not found message: {Reason}, inner: {Inner}", result.Error.Message, result.Inner);
            return null;
        }

        var playerStates = new List<BlackJackPlayerState>(participatingPlayers.Length);
        foreach (var member in participatingPlayers)
        {
            var textChannel = await CreatePlayerChannel(guild.Value, botUser, member, guildApi, logger);
            var memberTip =
                await SendRawRequest<object>($"/tip/{member.User.Value.ID.Value}", ApiRequestType.Get, null)
                    .ReceiveJson<RawTip>();
            playerStates.Add(new BlackJackPlayerState
            {
                GameId = matchData.GameId ?? "",
                PlayerId = member.User.Value.ID.Value,
                PlayerName = member.GetDisplayName(),
                TextChannel = textChannel,
                OwnTips = memberTip?.Amount ?? 0,
                BetTips = (int) MathF.Floor(matchData.BaseBet ?? 0.0f),
                Order = 0,
                AvatarUrl = member.GetAvatarUrl()
            });
        }

        return (matchData, playerStates.ToImmutableArray());
    }

    public async Task<(GenericJoinStatus, ImmutableArray<T>)?> WaitForGame<T>(
        GenericJoinStatus waitingData,
        InteractionContext context,
        IEnumerable<Snowflake> mentionedPlayers,
        IUser botUser,
        string description,
        string gameName,
        IDiscordRestInteractionAPI interactionApi,
        IDiscordRestGuildAPI guildApi,
        ILogger logger) where T: IPlayerState, new()
    {
        var guild = await Utilities.GetGuildFromContext(context, interactionApi, logger);
        if (!guild.HasValue)
            return null;

        var participatingPlayers =
            (await GetMembers(guild.Value, mentionedPlayers, guildApi, logger)).ToImmutableArray();
        
        var matchData = await GenericWaitForGame(waitingData, context, participatingPlayers, botUser, gameName,
            description,
            interactionApi, logger);
        if (matchData == null)
        {
            var result = await interactionApi
                .CreateFollowupMessageAsync(context.ApplicationID, context.Token,
                    "Sorry, but looks like there are not enough players waiting for a match!");
            if (!result.IsSuccess)
                logger.LogError("Failed to reply with match not found message: {Reason}, inner: {Inner}", result.Error.Message, result.Inner);
            return null;
        }

        var playerStates = new List<T>(participatingPlayers.Length);
        foreach (var member in participatingPlayers)
        {
            var textChannel = await CreatePlayerChannel(guild.Value, botUser, member, guildApi, logger);
            playerStates.Add(new T
            {
                AvatarUrl = member.GetAvatarUrl(),
                GameId = matchData.GameId ?? "",
                PlayerId = member.User.Value.ID.Value,
                PlayerName = member.GetDisplayName(),
                TextChannel = textChannel,
            });
        }
        
        return (matchData, playerStates.ToImmutableArray());
    }

    private static async Task<GenericJoinStatus?> ReceiveMatchData(WebSocket socketSession,
        CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        var receiveResult = await socketSession.ReceiveAsync(new Memory<byte>(buffer), token);
        var payloadText = Encoding.UTF8.GetString(buffer[..receiveResult.Count]);
        var matchData = JsonSerializer.Deserialize<GenericJoinStatus>(payloadText);
        return matchData;
    }

    public async Task<IChannel?> CreatePlayerChannel(
        Snowflake guild,
        IUser botUser,
        IGuildMember member,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        while (true)
        {
            var category = await CreateCategory(guild, guildApi, logger);

            if (category == null)
            {
                logger.LogError("Failed to create category");
                return null;
            }
            
            var channelCreateResult = await guildApi
                .CreateGuildChannelAsync(guild,
                    $"{member.GetDisplayName()}-All-Burst",
                    ChannelType.GuildText,
                    permissionOverwrites: new[]
                    {
                        new PermissionOverwrite(guild, PermissionOverwriteType.Role,
                            DiscordPermissionSet.Empty,
                            new DiscordPermissionSet(DiscordPermission.ViewChannel)),
                        new PermissionOverwrite(member.User.Value.ID, PermissionOverwriteType.Member,
                            PlayerPermissions,
                            DiscordPermissionSet.Empty),
                        new PermissionOverwrite(botUser.ID, PermissionOverwriteType.Member,
                            BotPermissions,
                            DiscordPermissionSet.Empty)
                    }, parentID: category.ID);

            if (!channelCreateResult.IsSuccess)
            {
                logger.LogError("Failed to create channel: {Reason}, inner: {Inner}",
                    channelCreateResult.Error.Message, channelCreateResult.Inner);
                return null;
            }

            var channel = channelCreateResult.Entity;
            await Task.Delay(TimeSpan.FromSeconds(1));
            
            return channel;
        }
    }

    private static (GenericJoinStatus?, string) HandleSuccessfulJoinStatus(IFlurlResponse response,
        string unit, GameType gameType)
    {
        var newJoinStatus = response.GetJsonAsync<GenericJoinStatus>().GetAwaiter().GetResult();
        newJoinStatus = newJoinStatus with { GameType = gameType };
        return newJoinStatus.StatusType switch
        {
            GenericJoinStatusType.Waiting =>
                (newJoinStatus,
                    $"Successfully started a game with {newJoinStatus.PlayerIds.Count} initial {unit}! Please wait for matching..."),
            GenericJoinStatusType.Start => (newJoinStatus,
                $"Successfully started a game with {newJoinStatus.PlayerIds.Count} initial {unit}!"),
            GenericJoinStatusType.Matched =>
                (newJoinStatus,
                    $"Successfully matched a game with {newJoinStatus.PlayerIds.Count} players! Preparing the game..."),
            GenericJoinStatusType.TimedOut =>
                (null, "Matching has timed out. No match found."),
            var invalid => (null, $"Unknown status: {invalid}")
        };
    }

    private async Task<IChannel?> CreateCategory(
        Snowflake guild,
        IDiscordRestGuildAPI guildApi,
        ILogger logger)
    {
        if (_guildChannelList.ContainsKey(guild.Value))
        {
            var category = _guildChannelList[guild.Value]
                .Where(c => c.Name.Value.ToLowerInvariant().Equals(CategoryName.ToLowerInvariant()))
                .ToImmutableArray();
            if (!category.IsEmpty) return category.First();

            var createResult = await guildApi
                .CreateGuildChannelAsync(guild, CategoryName, ChannelType.GuildCategory);

            if (!createResult.IsSuccess)
            {
                logger.LogError("Failed to create channel category: {Reason}, inner: {Inner}",
                    createResult.Error.Message, createResult.Inner);
                return null;
            }

            var newCategory = createResult.Entity;
            _guildChannelList[guild.Value].Add(newCategory);
            return newCategory;
        }

        var channels = await guildApi.GetGuildChannelsAsync(guild);
        if (!channels.IsSuccess)
        {
            logger.LogError("Failed to get guild channels: {Reason}, inner: {Inner}",
                channels.Error.Message, channels.Inner);
            return null;
        }
        
        _guildChannelList.GetOrAdd(guild.Value, channels.Entity.ToList());

        var retrievedCategory = channels
            .Entity
            .Where(c => c.Name.Value.ToLowerInvariant().Equals(CategoryName.ToLowerInvariant()))
            .ToImmutableArray();
        if (!retrievedCategory.IsEmpty) return retrievedCategory.First();

        {
            var createResult = await guildApi
                .CreateGuildChannelAsync(guild, CategoryName, ChannelType.GuildCategory);

            if (!createResult.IsSuccess)
            {
                logger.LogError("Failed to create channel category: {Reason}, inner: {Inner}",
                    createResult.Error.Message, createResult.Inner);
                return null;
            }

            var newCategory = createResult.Entity;
            _guildChannelList[guild.Value].Add(newCategory);
            return newCategory;
        }
    }

    private static async Task<IFlurlResponse> Post<TPayloadType>(string url, TPayloadType? payload)
    {
        if (payload == null)
            throw new ArgumentException("The payload cannot be null when sending POST requests.");

        return await url.PostJsonAsync(payload);
    }
    
    private static async Task<IFlurlResponse> Patch<TPayloadType>(string url, TPayloadType? payload)
    {
        if (payload == null)
            throw new ArgumentException("The payload cannot be null when sending POST requests.");

        return await url.PatchJsonAsync(payload);
    }
}