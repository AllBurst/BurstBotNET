using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BurstBotShared.Shared;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Data.Serializables;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Utilities = BurstBotShared.Shared.Utilities.Utilities;

namespace BurstBotShared.Api;

public class BurstApi
{
    private const int BufferSize = 2048;
    private const string CategoryName = "All-Burst-Category";
    private readonly string _serverEndpoint;
    private readonly int _socketPort;
    private readonly string _socketEndpoint;
    private readonly ConcurrentDictionary<ulong, List<DiscordChannel>> _guildChannelList = new();

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

    public BurstApi(Config config)
    {
        _serverEndpoint = config.ServerEndpoint;
        _socketEndpoint = config.SocketEndpoint;
        _socketPort = config.SocketPort;
    }

    public async Task<IFlurlResponse> SendRawRequest<TPayloadType>(string endpoint, ApiRequestType requestType, TPayloadType? payload)
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

    public async Task<(BlackJackJoinStatus, BlackJackPlayerState)?> WaitForBlackJackGame(BlackJackJoinStatus waitingData,
        InteractionCreateEventArgs e,
        DiscordMember invokingMember,
        DiscordUser botUser,
        string description,
        State state,
        ILogger logger)
    {
        using var socketSession = new ClientWebSocket();
        socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        using var cancellationTokenSource = new CancellationTokenSource();
        var url = new Uri(_socketPort != 0 ? $"ws://{_socketEndpoint}:{_socketPort}" : $"wss://{_socketEndpoint}");
        await socketSession.ConnectAsync(url, cancellationTokenSource.Token);

        while (true)
        {
            if (socketSession.State == WebSocketState.Open)
                break;
        }

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(waitingData)),
            WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationTokenSource.Token);
        
        while (true)
        {
            var receiveTask = ReceiveMatchData(socketSession, cancellationTokenSource.Token);
            var timeoutTask = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                return new BlackJackJoinStatus
                {
                    SocketIdentifier = null,
                    GameId = null,
                    StatusType = BlackJackJoinStatusType.TimedOut
                };
            }, cancellationTokenSource.Token);

            var matchData = await await Task.WhenAny(new[] { receiveTask!, timeoutTask });

            if (matchData.StatusType.Equals(BlackJackJoinStatusType.TimedOut))
            {
                const string message = "Timeout because no match game is found";
                logger.LogDebug(message);
                await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, message,
                    cancellationTokenSource.Token);
                break;
            }

            if (matchData.StatusType != BlackJackJoinStatusType.Matched || matchData.GameId == null) continue;
            
            await socketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "Matched.",
                cancellationTokenSource.Token);
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .AddEmbed(Utilities.BuildBlackJackEmbed(invokingMember, botUser, matchData, description,
                        null)));

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

        return null;
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

            var channel = await guild.CreateTextChannelAsync($"{invokingMember.DisplayName}-All-Burst", category, overwrites: new[] { denyEveryone, permitPlayer, permitBot });

            await Task.Delay(TimeSpan.FromSeconds(1));
            if (channel == null)
                continue;

            return channel;
        }
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

    private static async Task<BlackJackJoinStatus?> ReceiveMatchData(WebSocket socketSession, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        var receiveResult = await socketSession.ReceiveAsync(new Memory<byte>(buffer), token);
        var payloadText = Encoding.UTF8.GetString(buffer[..receiveResult.Count]);
        var matchData = JsonSerializer.Deserialize<BlackJackJoinStatus>(payloadText);
        return matchData;
    }
}