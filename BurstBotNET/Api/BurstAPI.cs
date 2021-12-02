using System.Net.WebSockets;
using System.Text.Json;
using BurstBotNET.Commands.BlackJack;
using BurstBotNET.Shared;
using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Data;
using BurstBotNET.Shared.Models.Data.Serializables;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Game.BlackJack;
using BurstBotNET.Shared.Models.Game.BlackJack.Serializables;
using BurstBotNET.Shared.Models.Localization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Utilities = BurstBotNET.Shared.Utilities.Utilities;

namespace BurstBotNET.Api;

public class BurstApi
{
    private readonly string _serverEndpoint;
    private readonly int _socketPort;
    private readonly string _socketEndpoint;

    private const Permissions DefaultPlayerPermissions
        = Permissions.AddReactions
          | Permissions.EmbedLinks
          | Permissions.ReadMessageHistory
          | Permissions.SendMessages
          | Permissions.UseExternalEmojis
          | Permissions.AccessChannels;

    private const Permissions DefaultBotPermissions
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
        return requestType switch
        {
            ApiRequestType.Get => await url.GetAsync(),
            ApiRequestType.Post => await Post(url, payload),
            _ => throw new InvalidOperationException("Unsupported raw request type.")
        };
    }

    public async Task<IFlurlResponse> GetReward(PlayerRewardType rewardType, long playerId)
    {
        var endpoint = rewardType switch
        {
            PlayerRewardType.Daily => $"{_serverEndpoint}/player/{playerId}/daily",
            PlayerRewardType.Weekly => $"{_serverEndpoint}/player/{playerId}/weekly",
            _ => throw new ArgumentException("Incorrect reward type.")
        };

        return await endpoint.GetAsync();
    }

    public async Task WaitForGame(
        BlackJack blackJack,
        BlackJackJoinStatus waitingData,
        InteractionCreateEventArgs e,
        DiscordMember invokingMember,
        DiscordUser botUser,
        string description,
        GameStates gameStates,
        Config config,
        Localizations localizations,
        ILogger logger)
    {
        using var socketSession = new ClientWebSocket();
        socketSession.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var cancellationTokenSource = new CancellationTokenSource();
        await socketSession.ConnectAsync(new Uri($"wss://{_socketEndpoint}:{_socketPort}"), cancellationTokenSource.Token);

        while (true)
        {
            if (socketSession.State == WebSocketState.Open)
                break;
        }

        await socketSession.SendAsync(new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(waitingData)),
            WebSocketMessageType.Text, WebSocketMessageFlags.None, cancellationTokenSource.Token);
        
        while (true)
        {
            var buffer = new byte[2048];
            await socketSession.ReceiveAsync(new Memory<byte>(buffer), cancellationTokenSource.Token);
            var matchData = JsonSerializer.Deserialize<BlackJackJoinStatus>(buffer);

            if (matchData?.StatusType == BlackJackJoinStatusType.Matched && matchData.GameId != null)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .AddEmbed(Utilities.BuildBlackJackEmbed(invokingMember, botUser, matchData, description,
                            null)));

                var guild = e.Interaction.Guild;
                var textChannel = await CreatePlayerChannel(guild, invokingMember);
                var invokerTip = await SendRawRequest<object>($"/tip/{invokingMember.Id}", ApiRequestType.Get, null)
                    .ReceiveJson<RawTip>();
                await BlackJack.AddBlackJackPlayerState(matchData.GameId ?? "", new BlackJackPlayerState
                {
                    GameId = matchData.GameId ?? "",
                    PlayerId = invokingMember.Id,
                    PlayerName = invokingMember.DisplayName,
                    TextChannel = textChannel,
                    OwnTips = invokerTip?.Amount ?? 0,
                    BetTips = Constants.StartingBet,
                    Order = 0,
                    AvatarUrl = invokingMember.GetAvatarUrl(ImageFormat.Auto)
                }, gameStates);
                _ = Task.Run(() =>
                    blackJack.StartListening(matchData.GameId ?? "", config, gameStates, guild, localizations, logger));
            }
        }
    }

    public async Task<DiscordChannel> CreatePlayerChannel(DiscordGuild guild, DiscordMember invokingMember)
    {
        while (true)
        {
            var category = await CreateCategory(guild);
            var botMember = guild.CurrentMember;
            var denyEveryone = new DiscordOverwriteBuilder(guild.EveryoneRole).Allow(Permissions.None)
                .Deny(Permissions.AccessChannels);
            var permitPlayer = new DiscordOverwriteBuilder(invokingMember).Allow(DefaultPlayerPermissions)
                .Deny(Permissions.None);
            var permitBot = new DiscordOverwriteBuilder(botMember).Allow(DefaultBotPermissions)
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
        return await guild.CreateChannelCategoryAsync("All-Burst-Category");
    }

    private static async Task<IFlurlResponse> Post<TPayloadType>(string url, TPayloadType? payload)
    {
        if (payload == null)
            throw new ArgumentException("The payload cannot be null when sending POST requests.");

        return await url.PostJsonAsync(payload);
    }
}