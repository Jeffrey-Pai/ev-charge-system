namespace EvChargeSystem.Api.Messaging;

public record StartRequestedEvent(int SessionId, int ChargerId, int UserId, DateTime RequestedAtUtc);

public record StopRequestedEvent(int SessionId, decimal MeterEndKwh, DateTime RequestedAtUtc);

public record ReservationCreatedEvent(int ReservationId, int ChargerId, int UserId, DateTime StartAtUtc, DateTime EndAtUtc);

public record ReservationExpireRequestedEvent(int ReservationId, DateTime RequestedAtUtc);

public record ReservationConfirmedEvent(int ReservationId, int ChargerId, int UserId, DateTime ConfirmedAtUtc);

public record ReservationInUseEvent(int ReservationId, int ChargerId, int UserId, int SessionId, DateTime InUseAtUtc);

public record ReservationCompletedEvent(int ReservationId, int ChargerId, int UserId, int SessionId, DateTime CompletedAtUtc);

public record ReservationExpiredEvent(int ReservationId, int ChargerId, int UserId, DateTime ExpiredAtUtc);

public record ChargingStartedEvent(int SessionId, int ChargerId, int UserId, DateTime StartedAtUtc);

public record ChargingStoppedEvent(int SessionId, int ChargerId, int UserId, DateTime EndedAtUtc, decimal MeterEndKwh);
