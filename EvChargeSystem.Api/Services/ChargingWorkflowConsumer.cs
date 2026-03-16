using System.Text;
using System.Text.Json;
using EvChargeSystem.Api.Data;
using EvChargeSystem.Api.Messaging;
using EvChargeSystem.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EvChargeSystem.Api.Services;

public class ChargingWorkflowConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<ChargingWorkflowConsumer> logger,
    IEventBus eventBus) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await InitializeAsync(stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (_, ea) =>
                {
                    var ok = await HandleMessageAsync(ea, stoppingToken);
                    if (ok)
                    {
                        _channel!.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                };

                _channel!.BasicConsume(_options.Queue, autoAck: false, consumer);
                logger.LogInformation("Charging workflow consumer started");

                while (!stoppingToken.IsCancellationRequested && _connection is { IsOpen: true } && _channel is { IsOpen: true })
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consumer loop failed, retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                _channel?.Dispose();
                _connection?.Dispose();
                _channel = null;
                _connection = null;
            }
        }
    }

    private async Task<bool> HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        try
        {
            var routingKey = ea.RoutingKey;
            var bodyText = Encoding.UTF8.GetString(ea.Body.Span);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();

            switch (routingKey)
            {
                case "charging.start.requested":
                {
                    var payload = JsonSerializer.Deserialize<StartRequestedEvent>(bodyText);
                    if (payload is null) return false;

                    var session = await db.ChargingSessions
                        .Include(x => x.Charger)
                        .Include(x => x.Reservation)
                        .FirstOrDefaultAsync(x => x.Id == payload.SessionId, cancellationToken);
                    if (session is null || session.Charger is null) return false;

                    if (session.Status == SessionStatus.Active)
                    {
                        return true;
                    }

                    if (session.Status != SessionStatus.PendingStart)
                    {
                        logger.LogWarning("Ignoring start.requested for session {SessionId} in status {Status}", session.Id, session.Status);
                        return true;
                    }

                    session.Status = SessionStatus.Active;
                    session.Charger.Status = ChargerStatus.Charging;
                    session.Charger.UpdatedAtUtc = DateTime.UtcNow;

                    if (session.Reservation is not null && session.Reservation.Status == ReservationStatus.Confirmed)
                    {
                        session.Reservation.Status = ReservationStatus.InUse;
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    if (session.Reservation is not null && session.Reservation.Status == ReservationStatus.InUse)
                    {
                        await eventBus.PublishAsync(
                            "charging.reservation.in_use",
                            new ReservationInUseEvent(
                                session.Reservation.Id,
                                session.ChargerId,
                                session.UserAccountId,
                                session.Id,
                                DateTime.UtcNow),
                            cancellationToken);
                    }

                    await eventBus.PublishAsync(
                        "charging.started",
                        new ChargingStartedEvent(session.Id, session.ChargerId, session.UserAccountId, DateTime.UtcNow),
                        cancellationToken);
                    break;
                }
                case "charging.stop.requested":
                {
                    var payload = JsonSerializer.Deserialize<StopRequestedEvent>(bodyText);
                    if (payload is null) return false;

                    var session = await db.ChargingSessions
                        .Include(x => x.Charger)
                        .Include(x => x.Reservation)
                        .FirstOrDefaultAsync(x => x.Id == payload.SessionId, cancellationToken);
                    if (session is null || session.Charger is null) return false;

                    if (session.Status == SessionStatus.Completed)
                    {
                        return true;
                    }

                    if (session.Status != SessionStatus.PendingStop)
                    {
                        logger.LogWarning("Ignoring stop.requested for session {SessionId} in status {Status}", session.Id, session.Status);
                        return true;
                    }

                    session.Status = SessionStatus.Completed;
                    session.EndedAtUtc ??= DateTime.UtcNow;
                    session.MeterEndKwh = payload.MeterEndKwh;

                    session.Charger.Status = ChargerStatus.Available;
                    session.Charger.UpdatedAtUtc = DateTime.UtcNow;

                    if (session.Reservation is not null)
                    {
                        session.Reservation.Status = ReservationStatus.Completed;
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    if (session.Reservation is not null)
                    {
                        await eventBus.PublishAsync(
                            "charging.reservation.completed",
                            new ReservationCompletedEvent(
                                session.Reservation.Id,
                                session.ChargerId,
                                session.UserAccountId,
                                session.Id,
                                DateTime.UtcNow),
                            cancellationToken);
                    }

                    await eventBus.PublishAsync(
                        "charging.stopped",
                        new ChargingStoppedEvent(
                            session.Id,
                            session.ChargerId,
                            session.UserAccountId,
                            session.EndedAtUtc.Value,
                            session.MeterEndKwh ?? payload.MeterEndKwh),
                        cancellationToken);
                    break;
                }
                case "charging.reservation.created":
                {
                    var payload = JsonSerializer.Deserialize<ReservationCreatedEvent>(bodyText);
                    if (payload is null) return false;

                    var reservation = await db.ChargingReservations
                        .Include(x => x.Charger)
                        .FirstOrDefaultAsync(x => x.Id == payload.ReservationId, cancellationToken);

                    if (reservation is null || reservation.Charger is null)
                    {
                        return false;
                    }

                    if (reservation.Status is ReservationStatus.Completed or ReservationStatus.Cancelled or ReservationStatus.Expired)
                    {
                        logger.LogWarning("Ignoring reservation.created for reservation {ReservationId} in status {Status}", reservation.Id, reservation.Status);
                        return true;
                    }

                    if (reservation.Charger.Status == ChargerStatus.Available)
                    {
                        reservation.Charger.Status = ChargerStatus.Reserved;
                        reservation.Charger.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }

                    await eventBus.PublishAsync(
                        "charging.reservation.confirmed",
                        new ReservationConfirmedEvent(
                            reservation.Id,
                            reservation.ChargerId,
                            reservation.UserAccountId,
                            DateTime.UtcNow),
                        cancellationToken);

                    break;
                }
                case "charging.reservation.expire.requested":
                {
                    var payload = JsonSerializer.Deserialize<ReservationExpireRequestedEvent>(bodyText);
                    if (payload is null) return false;

                    var reservation = await db.ChargingReservations
                        .Include(x => x.Charger)
                        .FirstOrDefaultAsync(x => x.Id == payload.ReservationId, cancellationToken);

                    if (reservation is null || reservation.Charger is null)
                    {
                        logger.LogWarning("Reservation {ReservationId} not found for expire.requested", payload.ReservationId);
                        return true;
                    }

                    if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed or ReservationStatus.Expired)
                    {
                        return true;
                    }

                    if (reservation.Status == ReservationStatus.InUse)
                    {
                        logger.LogWarning("Ignoring expire.requested for reservation {ReservationId} because it is in use", reservation.Id);
                        return true;
                    }

                    if (reservation.EndAtUtc > DateTime.UtcNow)
                    {
                        logger.LogWarning("Ignoring expire.requested for reservation {ReservationId} because it has not passed end time", reservation.Id);
                        return true;
                    }

                    reservation.Status = ReservationStatus.Expired;

                    var hasFutureConfirmedReservations = await db.ChargingReservations.AnyAsync(x =>
                        x.ChargerId == reservation.ChargerId &&
                        x.Id != reservation.Id &&
                        x.Status == ReservationStatus.Confirmed,
                        cancellationToken);

                    var hasInProgressSessions = await db.ChargingSessions.AnyAsync(x =>
                        x.ChargerId == reservation.ChargerId &&
                        (x.Status == SessionStatus.PendingStart || x.Status == SessionStatus.Active || x.Status == SessionStatus.PendingStop),
                        cancellationToken);

                    if (!hasFutureConfirmedReservations && !hasInProgressSessions && reservation.Charger.Status == ChargerStatus.Reserved)
                    {
                        reservation.Charger.Status = ChargerStatus.Available;
                        reservation.Charger.UpdatedAtUtc = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    await eventBus.PublishAsync(
                        "charging.reservation.expired",
                        new ReservationExpiredEvent(
                            reservation.Id,
                            reservation.ChargerId,
                            reservation.UserAccountId,
                            DateTime.UtcNow),
                        cancellationToken);

                    break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process event {RoutingKey}", ea.RoutingKey);
            return false;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            DispatchConsumersAsync = true
        };

        var retries = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();
                _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
                _channel.QueueDeclare(_options.Queue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(_options.Queue, _options.Exchange, "charging.start.requested");
                _channel.QueueBind(_options.Queue, _options.Exchange, "charging.stop.requested");
                _channel.QueueBind(_options.Queue, _options.Exchange, "charging.reservation.created");
                _channel.QueueBind(_options.Queue, _options.Exchange, "charging.reservation.expire.requested");
                return;
            }
            catch (Exception ex)
            {
                retries++;
                logger.LogWarning(ex, "Consumer init attempt {Attempt} failed", retries);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
