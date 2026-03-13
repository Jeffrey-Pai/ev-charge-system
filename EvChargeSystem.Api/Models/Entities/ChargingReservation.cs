namespace EvChargeSystem.Api.Models.Entities;

public class ChargingReservation
{
    public int Id { get; set; }
    public int ChargerId { get; set; }
    public int UserAccountId { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Charger? Charger { get; set; }
    public UserAccount? UserAccount { get; set; }
}
