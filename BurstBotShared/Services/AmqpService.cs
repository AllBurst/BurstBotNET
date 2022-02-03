using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BurstBotShared.Shared.Models.Config;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BurstBotShared.Services;

public sealed class AmqpService : IDisposable
{
    private const string BurstMatchExchangeName = "burst_match";
    private const string BurstGameExchangeName = "burst_game";

    private static readonly Dictionary<GameType, string> RoutingKeys = new()
    {
        { GameType.BlackJack, "match.requests.blackjack" },
        { GameType.ChinesePoker, "match.requests.chinese_poker" },
        { GameType.NinetyNine, "match.requests.ninety_nine" },
        { GameType.OldMaid, "match.requests.old_maid" },
        { GameType.RedDotsPicking, "match.requests.red_dots_picking" },
        { GameType.ChaseThePig, "match.requests.chase_the_pig" },
    };

    private readonly IConnection _publishConnection;
    private readonly IConnection _subscribeConnection;
    private readonly ILogger<AmqpService> _logger;
    private readonly ConcurrentQueue<IModel> _publishChannels = new();
    private readonly ConcurrentQueue<IModel> _subscribeChannels = new();

    private bool _disposed;

    public AmqpService(Config config)
    {
        var factory = new ConnectionFactory
        {
            HostName = config.Rabbit.Endpoint,
            DispatchConsumersAsync = true,
            UserName = config.Rabbit.Username,
            Password = config.Rabbit.Password
        };

        _publishConnection = factory.CreateConnection();
        _subscribeConnection = factory.CreateConnection();
        
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<AmqpService>();

        var connectionAndPools = new[]
            { (_publishConnection, _publishChannels), (_subscribeConnection, _subscribeChannels) };

        foreach (var (connection, queue) in connectionAndPools)
        {
            for (var i = 0; i < Environment.ProcessorCount / 2; i++)
            {
                var channel = connection.CreateModel();
                channel.ExchangeDeclare(BurstMatchExchangeName, ExchangeType.Topic);
                channel.ExchangeDeclare(BurstGameExchangeName, ExchangeType.Topic);
                queue.Enqueue(channel);
            }
        }

        _logger.LogInformation("AMQP connection successfully initiated");
    }

    public async Task RequestMatch(GameType gameType, byte[] waitingData)
    {
        var dequeueResult = false;
        while (!dequeueResult)
        {
            dequeueResult = _publishChannels.TryDequeue(out var channel);
            if (!dequeueResult)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }
            
            channel.BasicPublish(BurstMatchExchangeName, RoutingKeys[gameType], null,
                waitingData);
            _publishChannels.Enqueue(channel!);
        }
    }

    public async Task<GenericJoinStatus?> ReceiveMatchData(string socketIdentifier)
    {
        var dequeueResult = _subscribeChannels.TryDequeue(out var channel);
        var cancellationTokenSource = new CancellationTokenSource();
        while (!dequeueResult)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
            dequeueResult = _subscribeChannels.TryDequeue(out channel);
        }
        
        var queue = channel!.QueueDeclare().QueueName;
        channel.QueueBind(queue, BurstMatchExchangeName, $"match.responses.{socketIdentifier}");
        var consumer = new AsyncEventingBasicConsumer(channel);
        var payloadChannel = Channel.CreateBounded<GenericJoinStatus>(5);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var payloadText = Encoding.UTF8.GetString(body);
                var matchData = JsonSerializer.Deserialize<GenericJoinStatus>(payloadText);
                await payloadChannel.Writer.WriteAsync(matchData!, cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to receive match data from queue: {Exception}, socket identifier: {Identifier}", ex, socketIdentifier);
            }
        };
        channel.BasicConsume(consumer, queue, true, socketIdentifier);
        var channelNumber = channel!.ChannelNumber;
        _subscribeChannels.Enqueue(channel);

        while (true)
        {
            var receiveTask = ReceiveMatchData(payloadChannel, cancellationTokenSource.Token);
            var timeoutCancellationTokenSource = new CancellationTokenSource();
            var timeoutTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), timeoutCancellationTokenSource.Token);
                    var subscribedChannel = _subscribeChannels
                        .First(c => c.ChannelNumber == channelNumber);
                    subscribedChannel.BasicCancel(socketIdentifier);
                    subscribedChannel.QueueUnbind(queue, BurstMatchExchangeName, $"match.responses.{socketIdentifier}");
                    subscribedChannel.QueueDelete(queue);
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

            var matchData = await await Task.WhenAny(receiveTask, timeoutTask);
            if (matchData is { StatusType: GenericJoinStatusType.TimedOut })
            {
                _ = Task.Run(() =>
                {
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                });
                
                break;
            }

            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
                var subscribedChannel = _subscribeChannels
                    .First(c => c.ChannelNumber == channelNumber);
                subscribedChannel.BasicCancel(socketIdentifier);
                subscribedChannel.QueueUnbind(queue, BurstMatchExchangeName, $"match.responses.{socketIdentifier}");
                subscribedChannel.QueueDelete(queue);
            });
            
            if (matchData is not { StatusType: GenericJoinStatusType.Matched } || matchData.GameId == null) continue;
            cancellationTokenSource.Dispose();

            return matchData;
        }

        return null;
    } 

    ~AmqpService()
    {
        Dispose(false);
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private static async Task<GenericJoinStatus?> ReceiveMatchData(Channel<GenericJoinStatus, GenericJoinStatus> channel, CancellationToken ct)
    {
        var matchData = await channel.Reader.ReadAsync(ct);
        return matchData;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            var pools = new[] { _publishChannels, _subscribeChannels };
            foreach (var pool in pools)
            {
                foreach (var channel in pool)
                {
                    channel.Close();
                    channel.Dispose();
                }
            }
            
            _publishConnection.Close();
            _publishConnection.Dispose();
            _subscribeConnection.Close();
            _subscribeConnection.Dispose();
        }

        _disposed = true;
    }
}