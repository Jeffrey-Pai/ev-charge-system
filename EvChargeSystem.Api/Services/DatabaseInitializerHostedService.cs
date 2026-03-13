using EvChargeSystem.Api.Data;
using EvChargeSystem.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Services;

public class DatabaseInitializerHostedService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DatabaseInitializerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retries = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();

                await db.Database.EnsureCreatedAsync(stoppingToken);

                if (!await db.Chargers.AnyAsync(stoppingToken))
                {
                    db.Chargers.AddRange(
                        new Charger { Code = "CP-001", Location = "B1-Area", Status = ChargerStatus.Available },
                        new Charger { Code = "CP-002", Location = "B1-Area", Status = ChargerStatus.Available },
                        new Charger { Code = "CP-003", Location = "B2-Area", Status = ChargerStatus.Available });
                }

                if (!await db.UserAccounts.AnyAsync(stoppingToken))
                {
                    db.UserAccounts.Add(new UserAccount
                    {
                        UserName = configuration["Seed:DemoUserName"] ?? "demo",
                        ApiKey = configuration["Seed:DemoApiKey"] ?? "demo-api-key-12345",
                        IsActive = true
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Database initialized and seed data inserted");
                return;
            }
            catch (Exception ex)
            {
                retries++;
                logger.LogWarning(ex, "Database init attempt {Attempt} failed", retries);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
