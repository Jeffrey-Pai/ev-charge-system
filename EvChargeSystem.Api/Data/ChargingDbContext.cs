using EvChargeSystem.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvChargeSystem.Api.Data;

public class ChargingDbContext(DbContextOptions<ChargingDbContext> options) : DbContext(options)
{
    public DbSet<Charger> Chargers => Set<Charger>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<ChargingSession> ChargingSessions => Set<ChargingSession>();
    public DbSet<ChargingReservation> ChargingReservations => Set<ChargingReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Charger>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.HasIndex(x => x.ApiKey).IsUnique();
            entity.Property(x => x.UserName).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ApiKey).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<ChargingReservation>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(x => x.Charger)
                .WithMany(x => x.Reservations)
                .HasForeignKey(x => x.ChargerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.Reservations)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChargingSession>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(x => x.Charger)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.ChargerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.UserAccount)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.UserAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Reservation)
                .WithMany()
                .HasForeignKey(x => x.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        base.OnModelCreating(modelBuilder);
    }
}
