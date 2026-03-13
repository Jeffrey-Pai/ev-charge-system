namespace EvChargeSystem.Api.Models.Entities;

public class UserAccount
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChargingSession> Sessions { get; set; } = new List<ChargingSession>();
    public ICollection<ChargingReservation> Reservations { get; set; } = new List<ChargingReservation>();
}
