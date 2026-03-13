namespace EvChargeSystem.Api.Models.Entities;

public enum ChargerStatus
{
    Available,
    Reserved,
    Charging,
    Offline
}

public enum SessionStatus
{
    PendingStart,
    Active,
    PendingStop,
    Completed,
    Failed
}

public enum ReservationStatus
{
    Confirmed,
    InUse,
    Completed,
    Cancelled,
    Expired
}
