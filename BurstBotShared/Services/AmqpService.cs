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
    private readonly ConcurrentDictionary<string, Channel<GenericJoinStatus>> _matchRequestChannels = new(10, 50);
    private readonly ConcurrentDictionary<string, Channel<byte[]>> _inGameResponseChannels = new(10, 50);

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

        _ = Task.Run(async () =>
        {
            await StartListeningForGameMatches();
            _logger.LogInformation("Successfully started listening for game matches");
        });

        _ = Task.Run(async () =>
        {
            await StartListeningForGameResponses();
            _logger.LogInformation("Successfully started listening for in-game responses");
        });

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
        var payloadChannel = _matchRequestChannels.AddOrUpdate(socketIdentifier, Channel.CreateBounded<GenericJoinStatus>(5),
            (_, _) => Channel.CreateBounded<GenericJoinStatus>(5));
        var cancellationTokenSource = new CancellationTokenSource();
        
        while (true)
        {
            var receiveTask = ReceiveMatchData(payloadChannel, cancellationTokenSource.Token);
            var timeoutCancellationTokenSource = new CancellationTokenSource();
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

            var matchData = await await Task.WhenAny(receiveTask, timeoutTask);
            if (matchData is { StatusType: GenericJoinStatusType.TimedOut })
            {
                _ = Task.Run(() =>
                {
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                }, default);

                _matchRequestChannels.TryRemove(socketIdentifier, out _);
                
                break;
            }

            _ = Task.Run(() =>
            {
                timeoutCancellationTokenSource.Cancel();
            }, default);
            
            if (matchData is not { StatusType: GenericJoinStatusType.Matched } || matchData.GameId == null) continue;
            cancellationTokenSource.Dispose();
            _matchRequestChannels.TryRemove(socketIdentifier, out _);

            return matchData;
        }

        return null;
    }

    public async Task SendInGameRequestData(byte[] payload, GameType gameType, string gameId)
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

            channel.BasicPublish(BurstGameExchangeName, $"game.{gameType}.{gameId}.requests", null,
                payload);
            _publishChannels.Enqueue(channel!);
        }
    }

    public void RegisterResponseChannel(string gameId, Channel<byte[]> channel)
    {
        _inGameResponseChannels.AddOrUpdate(gameId, channel, (_, _) => channel);
    }

    public void UnregisterResponseChannel(string gameId)
    {
        _inGameResponseChannels.TryRemove(gameId, out _);
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

    private async Task StartListeningForGameMatches()
    {
        var dequeueResult = _subscribeChannels.TryDequeue(out var channel);
        while (!dequeueResult)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), default);
            dequeueResult = _subscribeChannels.TryDequeue(out channel);
        }
        
        var queue = channel!.QueueDeclare().QueueName;
        channel.QueueBind(queue, BurstMatchExchangeName, "match.responses.*");
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var socketIdentifier = ea.RoutingKey[16..] ?? string.Empty;
                Console.WriteLine($"Raw match data: {Encoding.UTF8.GetString(ea.Body.Span)}");
                var matchData = JsonSerializer.Deserialize<GenericJoinStatus>(ea.Body.Span);
                var getChannelResult = _matchRequestChannels.TryGetValue(socketIdentifier, out var payloadChannel);
                if (getChannelResult)
                    await payloadChannel!.Writer.WriteAsync(matchData!);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to receive match data from queue: {Exception}, routing key: {Identifier}", ex, ea.RoutingKey);
            }
        };
        
        channel.BasicConsume(consumer, queue, true, "game.matches.responses.consumer");
        _subscribeChannels.Enqueue(channel!);
    }
    
    private async Task StartListeningForGameResponses()
    {
        var dequeueResult = _subscribeChannels.TryDequeue(out var channel);
        while (!dequeueResult)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), default);
            dequeueResult = _subscribeChannels.TryDequeue(out channel);
        }
        
        var queue = channel!.QueueDeclare().QueueName;
        channel.QueueBind(queue, BurstGameExchangeName, "game.*.*.responses");
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var lastDotIndex = ea.RoutingKey.LastIndexOf('.');
                var secondLastDotIndex = ea.RoutingKey.LastIndexOf('.', lastDotIndex - 1);
                var gameId = ea.RoutingKey[(secondLastDotIndex + 1)..lastDotIndex] ?? "";
                var getChannelResult = _inGameResponseChannels.TryGetValue(gameId, out var payloadChannel);
                if (getChannelResult)
                    await payloadChannel!.Writer.WriteAsync(ea.Body.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to receive match data from queue: {Exception}, routing key: {Identifier}", ex, ea.RoutingKey);
            }
        };
        
        channel.BasicConsume(consumer, queue, true, "game.responses.consumer");
        _subscribeChannels.Enqueue(channel!);
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