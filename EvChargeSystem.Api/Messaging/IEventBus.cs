namespace EvChargeSystem.Api.Messaging;

public interface IEventBus
{
    Task PublishAsync(string routingKey, object payload, CancellationToken cancellationToken = default);
}
