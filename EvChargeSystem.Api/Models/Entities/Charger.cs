namespace EvChargeSystem.Api.Models.Entities;

public class Charger
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public ChargerStatus Status { get; set; } = ChargerStatus.Available;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChargingSession> Sessions { get; set; } = new List<ChargingSession>();
    public ICollection<ChargingReservation> Reservations { get; set; } = new List<ChargingReservation>();
}
