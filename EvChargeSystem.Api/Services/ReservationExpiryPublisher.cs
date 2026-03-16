using EvChargeSystem.Api.Data;
using EvChargeSystem.Api.Messaging;
using EvChargeSystem.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Services;

public class ReservationExpiryPublisher(
    IServiceScopeFactory scopeFactory,
    IEventBus eventBus,
    ILogger<ReservationExpiryPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishExpiredReservationsAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish reservation expiry events");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PublishExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();

        var now = DateTime.UtcNow;
        var expiredReservationIds = await db.ChargingReservations
            .AsNoTracking()
            .Where(x => x.Status == ReservationStatus.Confirmed && x.EndAtUtc <= now)
            .OrderBy(x => x.EndAtUtc)
            .Select(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var reservationId in expiredReservationIds)
        {
            await eventBus.PublishAsync(
                "charging.reservation.expire.requested",
                new ReservationExpireRequestedEvent(reservationId, now),
                cancellationToken);
        }

        if (expiredReservationIds.Count > 0)
        {
            logger.LogInformation("Published {Count} reservation.expire.requested events", expiredReservationIds.Count);
        }
    }
}
