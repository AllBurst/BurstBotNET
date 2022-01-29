using BurstBotShared.Shared.Models.Config;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace BurstBotShared.Services;

public sealed class AmqpService : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<AmqpService> _logger;

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

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<AmqpService>();
        
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
            _connection.Dispose();
            _channel.Dispose();
        }

        _disposed = true;
    }
}