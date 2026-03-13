namespace EvChargeSystem.Api.Models.Entities;

public class ChargingSession
{
    public int Id { get; set; }
    public int ChargerId { get; set; }
    public int UserAccountId { get; set; }
    public int? ReservationId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public decimal MeterStartKwh { get; set; }
    public decimal? MeterEndKwh { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.PendingStart;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Charger? Charger { get; set; }
    public UserAccount? UserAccount { get; set; }
    public ChargingReservation? Reservation { get; set; }
}
