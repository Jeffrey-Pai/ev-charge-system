using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EvChargeSystem.Api.Messaging;

public class RabbitMqEventBus : IEventBus, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly object _syncLock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqEventBus(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventBus> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync(string routingKey, object payload, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var properties = _channel!.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange: _options.Exchange,
            routingKey: routingKey,
            basicProperties: properties,
            body: bytes);

        _logger.LogInformation("Published event {RoutingKey}", routingKey);
        return Task.CompletedTask;
    }

    private void EnsureConnected()
    {
        lock (_syncLock)
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            {
                return;
            }

            _channel?.Dispose();
            _connection?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                DispatchConsumersAsync = true
            };

            var retries = 0;
            while (retries < 8)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();
                    _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
                    return;
                }
                catch (Exception ex)
                {
                    retries++;
                    _logger.LogWarning(ex, "RabbitMQ connection attempt {Attempt} failed", retries);
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
            }

            throw new InvalidOperationException("Failed to connect to RabbitMQ");
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
