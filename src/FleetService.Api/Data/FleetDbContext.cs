using FleetService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FleetService.Api.Data;

public class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Vehicle>(e =>
        {
            e.Property(v => v.PlateNumber).HasMaxLength(16).IsRequired();
            e.HasIndex(v => v.PlateNumber).IsUnique();
            e.Property(v => v.Model).HasMaxLength(64).IsRequired();
        });
        b.Entity<ServiceRequest>(e =>
        {
            e.Property(r => r.Description).HasMaxLength(500).IsRequired();
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.Priority).HasConversion<string>();
            e.HasOne(r => r.Vehicle).WithMany(v => v.Requests).HasForeignKey(r => r.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.Status, r.DueAtUtc });
            // SQLite не хранит DateTimeKind: при чтении помечаем даты как UTC
            e.Property(r => r.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.Property(r => r.DueAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}
