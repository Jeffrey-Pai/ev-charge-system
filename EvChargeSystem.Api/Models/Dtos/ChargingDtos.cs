using EvChargeSystem.Api.Models.Entities;

namespace EvChargeSystem.Api.Models.Dtos;

public record ReserveChargingRequest(string ChargerCode, DateTime StartAtUtc, DateTime EndAtUtc);

public record StartChargingRequest(string ChargerCode, decimal MeterStartKwh);

public record StopChargingRequest(int SessionId, decimal MeterEndKwh);

public record SessionResponse(
    int SessionId,
    string ChargerCode,
    SessionStatus Status,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    decimal MeterStartKwh,
    decimal? MeterEndKwh,
    int? ReservationId);
