using System.Collections.Concurrent;
using BurstBotShared.Shared.Models.Config;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace BurstBotShared.Services;

public sealed class AmqpService : IDisposable
{
    private const string BurstMatchExchangeName = "burst_match";
    private const string BurstGameExchangeName = "burst_game";
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
            HostName = config.RabbitMqEndpoint,
            DispatchConsumersAsync = true,
            UserName = config.RabbitMqUsername,
            Password = config.RabbitMqPassword
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

    ~AmqpService()
    {
        Dispose(false);
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _publishConnection.Dispose();
            _subscribeConnection.Dispose();
            var pools = new[] { _publishChannels, _subscribeChannels };
            foreach (var pool in pools)
            {
                foreach (var channel in pool)
                    channel.Dispose();
            }
        }

        _disposed = true;
    }
}