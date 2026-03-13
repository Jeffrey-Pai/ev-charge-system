namespace EvChargeSystem.Api.Models.Dtos;

public record RegisterRequest(string UserName);

public record ValidateApiKeyRequest(string ApiKey);

public record AuthResponse(int UserId, string UserName, string ApiKey, bool IsValid);
