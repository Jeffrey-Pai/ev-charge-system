using EvChargeSystem.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Infrastructure;

public class ApiKeyMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (path.StartsWith("/swagger") || path.StartsWith("/api/auth") || path == "/")
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Missing X-Api-Key" });
            return;
        }

        var db = context.RequestServices.GetRequiredService<ChargingDbContext>();
        var user = await db.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == apiKey.ToString() && x.IsActive);

        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid API key" });
            return;
        }

        context.Items["UserId"] = user.Id;
        context.Items["UserName"] = user.UserName;
        await _next(context);
    }
}
