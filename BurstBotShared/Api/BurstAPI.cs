using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Config;
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

namespace BurstBotShared.Api;

public class BurstApi
{
    public const string CategoryName = "All-Burst-Category";
    private const int BufferSize = 2048;

    private const Permissions PlayerPermissions
        = Permissions.AddReactions
          | Permissions.EmbedLinks
          | Permissions.ReadMessageHistory
          | Permissions.SendMessages
          | Permissions.UseExternalEmojis
          | Permissions.AccessChannels;

    private const Permissions BotPermissions
        = Permissions.AddReactions
          | Permissions.EmbedLinks
          | Permissions.ReadMessageHistory
          | Permissions.SendMessages
          | Permissions.UseExternalEmojis
          | Permissions.AccessChannels
          | Permissions.ManageChannels
          | Permissions.ManageMessages;

    private readonly ConcurrentDictionary<ulong, List<DiscordChannel>> _guildChannelList = new();
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

    public async Task<GenericJoinStatus?> GenericWaitForGame(GenericJoinStatus waitingData,
        InteractionCreateEventArgs e,
        DiscordMember invokingMember,
        DiscordUser botUser,
        string gameName,
        string description,
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
            var receiveTask = ReceiveMatchData(socketSession, logger, cancellationTokenSource.Token);
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
                catch (TaskCanceledException ex)
                {
                    logger.LogDebug("Timeout task for matching has been cancelled: {@Exception}", ex);
                }
                finally
                {
                    timeoutCancellationTokenSource.Dispose();
                }

                return null;
            }, cancellationTokenSource.Token);

            var matchData = await await Task.WhenAny(new[] { receiveTask, timeoutTask });
            logger.LogDebug("Match data: {Data}", matchData?.ToString());
            if (matchData is { StatusType: GenericJoinStatusType.TimedOut })
            {
                const string message = "Timeout because no match game is found";
                logger.LogDebug(message);
                await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, message,
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

            await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "Matched.",
                cancellationTokenSource.Token);
            cancellationTokenSource.Dispose();
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, matchData, gameName, description,
                        null)));

            return matchData;
        }

        return null;
    }

    public static (GenericJoinStatus?, DiscordWebhookBuilder) HandleMatchGameHttpStatuses(IFlurlResponse response,
        string unit, GameType gameType)
    {
        var responseCode = response.ResponseMessage.StatusCode;
        return responseCode switch
        {
            HttpStatusCode.BadRequest =>
                (null,
                    new DiscordWebhookBuilder().WithContent(
                        "Sorry! It seems that at least one of the players you mentioned has already joined the waiting list!")),
            HttpStatusCode.NotFound =>
                (null,
                    new DiscordWebhookBuilder().WithContent(
                        "Sorry, but all players who want to join the game have to join the server first!")),
            HttpStatusCode.InternalServerError =>
                (null,
                    new DiscordWebhookBuilder().WithContent(
                        $"Sorry, but an unknown error occurred! Could you report this to the developers: **{responseCode}: {response.ResponseMessage.ReasonPhrase}**")),
            _ => HandleSuccessfulJoinStatus(response, unit, gameType)
        };
    }

    public static async Task<(DiscordMember[], GenericJoinStatus)?> HandleStartGameReactions(
        string gameName,
        InteractionCreateEventArgs e,
        DiscordMessage originalMessage,
        DiscordMember invokingMember,
        DiscordUser botUser,
        GenericJoinStatus joinStatus,
        IEnumerable<ulong> playerIds,
        string confirmationEndpoint,
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
                .AddEmbed(Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, gameName,
                    confirmedUsersString,
                    secondsRemained)));
        }

        if (cancelled)
            return null;

        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder()
            .WithContent(
                "<:burst_spade:910826637657010226> <:burst_heart:910826529511051284> **GAME STARTED!** <:burst_diamond:910826609576140821> <:burst_club:910826578336948234>"));

        var guild = e.Interaction.Guild;
        var members = await Task.WhenAll(confirmedUsers
            .Select(async u => await guild.GetMemberAsync(u.Id)));
        var matchData = await state.BurstApi
            .SendRawRequest(confirmationEndpoint, ApiRequestType.Post, new GenericJoinStatus
            {
                StatusType = GenericJoinStatusType.Start,
                PlayerIds = members.Select(m => m.Id).ToList()
            })
            .ReceiveJson<GenericJoinStatus>();

        return (members, matchData);
    }

    public async Task<(GenericJoinStatus, BlackJackPlayerState)?> WaitForBlackJackGame(GenericJoinStatus waitingData,
        InteractionCreateEventArgs e,
        DiscordMember invokingMember,
        DiscordUser botUser,
        string description,
        ILogger logger)
    {
        var matchData =
            await GenericWaitForGame(waitingData, e, invokingMember, botUser, "Black Jack", description, logger);

        if (matchData == null)
            return null;

        var guild = e.Interaction.Guild;
        var textChannel = await CreatePlayerChannel(guild, invokingMember);
        var invokerTip = await SendRawRequest<object>($"/tip/{invokingMember.Id}", ApiRequestType.Get, null)
            .ReceiveJson<RawTip>();
        return (matchData, new BlackJackPlayerState
        {
            GameId = matchData.GameId ?? "",
            PlayerId = invokingMember.Id,
            PlayerName = invokingMember.DisplayName,
            TextChannel = textChannel,
            OwnTips = invokerTip?.Amount ?? 0,
            BetTips = Constants.StartingBet,
            Order = 0,
            AvatarUrl = invokingMember.GetAvatarUrl(ImageFormat.Auto)
        });
    }

    public static async Task<GenericJoinStatus?> ReceiveMatchData(WebSocket socketSession, ILogger logger,
        CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        var receiveResult = await socketSession.ReceiveAsync(new Memory<byte>(buffer), token);
        var payloadText = Encoding.UTF8.GetString(buffer[..receiveResult.Count]);
        logger.LogDebug("Received match data from WS server: {Payload}", payloadText);
        var matchData = JsonSerializer.Deserialize<GenericJoinStatus>(payloadText);
        return matchData;
    }

    public async Task<DiscordChannel> CreatePlayerChannel(DiscordGuild guild, DiscordMember invokingMember)
    {
        while (true)
        {
            var category = await CreateCategory(guild);
            var botMember = guild.CurrentMember;
            var denyEveryone = new DiscordOverwriteBuilder(guild.EveryoneRole).Allow(Permissions.None)
                .Deny(Permissions.AccessChannels);
            var permitPlayer = new DiscordOverwriteBuilder(invokingMember).Allow(PlayerPermissions)
                .Deny(Permissions.None);
            var permitBot = new DiscordOverwriteBuilder(botMember).Allow(BotPermissions)
                .Deny(Permissions.None);

            var channel = await guild.CreateTextChannelAsync($"{invokingMember.DisplayName}-All-Burst", category,
                overwrites: new[] { denyEveryone, permitPlayer, permitBot });

            await Task.Delay(TimeSpan.FromSeconds(1));
            if (channel == null)
                continue;

            return channel;
        }
    }

    private static (GenericJoinStatus?, DiscordWebhookBuilder) HandleSuccessfulJoinStatus(IFlurlResponse response,
        string unit, GameType gameType)
    {
        var newJoinStatus = response.GetJsonAsync<GenericJoinStatus>().GetAwaiter().GetResult();
        newJoinStatus = newJoinStatus with { GameType = gameType };
        return newJoinStatus.StatusType switch
        {
            GenericJoinStatusType.Waiting =>
                (newJoinStatus,
                    new DiscordWebhookBuilder().WithContent(
                        $"Successfully started a game with {newJoinStatus.PlayerIds.Count} initial {unit}! Please wait for matching...")),
            GenericJoinStatusType.Start => (newJoinStatus,
                new DiscordWebhookBuilder().WithContent(
                    $"Successfully started a game with {newJoinStatus.PlayerIds.Count} initial {unit}!")),
            GenericJoinStatusType.Matched =>
                (newJoinStatus,
                    new DiscordWebhookBuilder().WithContent(
                        $"Successfully matched a game with {newJoinStatus.PlayerIds.Count} players! Preparing the game...")),
            GenericJoinStatusType.TimedOut =>
                (null, new DiscordWebhookBuilder().WithContent("Matching has timed out. No match found.")),
            var invalid => (null, new DiscordWebhookBuilder().WithContent($"Unknown status: {invalid}"))
        };
    }

    private async Task<DiscordChannel> CreateCategory(DiscordGuild guild)
    {
        if (_guildChannelList.ContainsKey(guild.Id))
        {
            var category = _guildChannelList[guild.Id]
                .Where(c => c.Name.ToLowerInvariant().Equals(CategoryName.ToLowerInvariant()))
                .ToImmutableList();
            if (!category.IsEmpty) return category.First();

            var newCategory = await guild.CreateChannelCategoryAsync(CategoryName);
            _guildChannelList[guild.Id].Add(newCategory);
            return newCategory;
        }

        var channels = await guild.GetChannelsAsync();
        _guildChannelList.GetOrAdd(guild.Id, channels.ToList());

        var retrievedCategory = channels
            .Where(c => c.Name.ToLowerInvariant().Equals(CategoryName.ToLowerInvariant()))
            .ToImmutableList();
        if (!retrievedCategory.IsEmpty) return retrievedCategory.First();

        {
            var newCategory = await guild.CreateChannelCategoryAsync(CategoryName);
            _guildChannelList[guild.Id].Add(newCategory);
            return newCategory;
        }
    }

    private static async Task<IFlurlResponse> Post<TPayloadType>(string url, TPayloadType? payload)
    {
        if (payload == null)
            throw new ArgumentException("The payload cannot be null when sending POST requests.");

        return await url.PostJsonAsync(payload);
    }
}