using EvChargeSystem.Api.Data;
using EvChargeSystem.Api.Models.Dtos;
using EvChargeSystem.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(ChargingDbContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            return BadRequest(new { message = "UserName is required" });
        }

        var exists = await db.UserAccounts.AnyAsync(x => x.UserName == userName, cancellationToken);
        if (exists)
        {
            return Conflict(new { message = "User already exists" });
        }

        var user = new UserAccount
        {
            UserName = userName,
            ApiKey = $"ev-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.UserAccounts.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new AuthResponse(user.Id, user.UserName, user.ApiKey, true));
    }

    [HttpPost("validate")]
    public async Task<ActionResult<AuthResponse>> Validate([FromBody] ValidateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var user = await db.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == request.ApiKey && x.IsActive, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new AuthResponse(0, string.Empty, request.ApiKey, false));
        }

        return Ok(new AuthResponse(user.Id, user.UserName, user.ApiKey, true));
    }
}
