using EvChargeSystem.Api.Data;
using EvChargeSystem.Api.Messaging;
using EvChargeSystem.Api.Models.Dtos;
using EvChargeSystem.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChargingController(ChargingDbContext db, IEventBus eventBus) : ControllerBase
{
    [HttpGet("chargers")]
    public async Task<IActionResult> GetChargers(CancellationToken cancellationToken)
    {
        var chargers = await db.Chargers
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Location,
                x.Status,
                x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(chargers);
    }

    [HttpPost("reservations")]
    public async Task<IActionResult> Reserve([FromBody] ReserveChargingRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        if (request.EndAtUtc <= request.StartAtUtc)
        {
            return BadRequest(new { message = "EndAtUtc must be later than StartAtUtc" });
        }

        var charger = await db.Chargers.FirstOrDefaultAsync(x => x.Code == request.ChargerCode, cancellationToken);
        if (charger is null)
        {
            return NotFound(new { message = "Charger not found" });
        }

        if (charger.Status == ChargerStatus.Charging || charger.Status == ChargerStatus.Offline)
        {
            return Conflict(new { message = "Charger is not reservable" });
        }

        var overlap = await db.ChargingReservations.AnyAsync(x =>
            x.ChargerId == charger.Id &&
            (x.Status == ReservationStatus.Confirmed || x.Status == ReservationStatus.InUse) &&
            request.StartAtUtc < x.EndAtUtc &&
            request.EndAtUtc > x.StartAtUtc,
            cancellationToken);

        if (overlap)
        {
            return Conflict(new { message = "Reservation time overlaps existing reservation" });
        }

        var reservation = new ChargingReservation
        {
            ChargerId = charger.Id,
            UserAccountId = userId.Value,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            Status = ReservationStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.ChargingReservations.Add(reservation);
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync("charging.reservation.created",
            new ReservationCreatedEvent(
                reservation.Id,
                reservation.ChargerId,
                reservation.UserAccountId,
                reservation.StartAtUtc,
                reservation.EndAtUtc),
            cancellationToken);

        return Ok(new
        {
            reservation.Id,
            reservation.ChargerId,
            reservation.UserAccountId,
            reservation.StartAtUtc,
            reservation.EndAtUtc,
            reservation.Status
        });
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartChargingRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var charger = await db.Chargers.FirstOrDefaultAsync(x => x.Code == request.ChargerCode, cancellationToken);
        if (charger is null)
        {
            return NotFound(new { message = "Charger not found" });
        }

        if (charger.Status == ChargerStatus.Charging || charger.Status == ChargerStatus.Offline)
        {
            return Conflict(new { message = "Charger is not available for start" });
        }

        var now = DateTime.UtcNow;
        var reservedWindow = await db.ChargingReservations
            .FirstOrDefaultAsync(x =>
                x.ChargerId == charger.Id &&
                x.Status == ReservationStatus.Confirmed &&
                x.StartAtUtc <= now &&
                x.EndAtUtc >= now,
                cancellationToken);

        if (reservedWindow is not null && reservedWindow.UserAccountId != userId.Value)
        {
            return Conflict(new { message = "Charger is reserved by another user/time window" });
        }

        var hasInProgressSession = await db.ChargingSessions.AnyAsync(x =>
            x.ChargerId == charger.Id &&
            (x.Status == SessionStatus.PendingStart || x.Status == SessionStatus.Active || x.Status == SessionStatus.PendingStop),
            cancellationToken);

        if (hasInProgressSession)
        {
            return Conflict(new { message = "Charger already has an in-progress session" });
        }

        var session = new ChargingSession
        {
            ChargerId = charger.Id,
            UserAccountId = userId.Value,
            ReservationId = reservedWindow?.Id,
            MeterStartKwh = request.MeterStartKwh,
            StartedAtUtc = now,
            Status = SessionStatus.PendingStart,
            CreatedAtUtc = now
        };

        db.ChargingSessions.Add(session);

        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            "charging.start.requested",
            new StartRequestedEvent(session.Id, session.ChargerId, session.UserAccountId, DateTime.UtcNow),
            cancellationToken);

        return Accepted(new SessionResponse(
            session.Id,
            charger.Code,
            session.Status,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.MeterStartKwh,
            session.MeterEndKwh,
            session.ReservationId));
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop([FromBody] StopChargingRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var session = await db.ChargingSessions
            .Include(x => x.Charger)
            .FirstOrDefaultAsync(x => x.Id == request.SessionId && x.UserAccountId == userId.Value, cancellationToken);

        if (session is null)
        {
            return NotFound(new { message = "Session not found" });
        }

        if (session.Status is SessionStatus.Completed or SessionStatus.Failed)
        {
            return Conflict(new { message = "Session already closed" });
        }

        if (session.Status == SessionStatus.PendingStart)
        {
            return Conflict(new { message = "Session is still starting, please retry shortly" });
        }

        if (session.Status != SessionStatus.Active)
        {
            return Conflict(new { message = "Session is not in active charging state" });
        }

        session.Status = SessionStatus.PendingStop;
        session.MeterEndKwh = request.MeterEndKwh;
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            "charging.stop.requested",
            new StopRequestedEvent(session.Id, request.MeterEndKwh, DateTime.UtcNow),
            cancellationToken);

        return Accepted(new
        {
            session.Id,
            session.Status,
            session.MeterEndKwh
        });
    }

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<IActionResult> GetSession(int sessionId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var session = await db.ChargingSessions
            .Include(x => x.Charger)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserAccountId == userId.Value, cancellationToken);

        if (session is null || session.Charger is null)
        {
            return NotFound(new { message = "Session not found" });
        }

        return Ok(new SessionResponse(
            session.Id,
            session.Charger.Code,
            session.Status,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.MeterStartKwh,
            session.MeterEndKwh,
            session.ReservationId));
    }

    private int? GetCurrentUserId()
    {
        if (HttpContext.Items.TryGetValue("UserId", out var value) && value is int userId)
        {
            return userId;
        }

        return null;
    }
}
