namespace EvChargeSystem.Api.Messaging;

public class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "charging.events";
    public string Queue { get; set; } = "charging.workflow.queue";
}
