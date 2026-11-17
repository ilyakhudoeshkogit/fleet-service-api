using FleetService.Api.Data;
using FleetService.Api.Models;
using FleetService.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FleetService.Api.Endpoints;

public static class FleetEndpoints
{
    public static void MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var vehicles = app.MapGroup("/api/vehicles").WithTags("Vehicles");

        vehicles.MapGet("/", async (FleetDbContext db, CancellationToken ct) =>
            await db.Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).Select(v => v.ToDto()).ToListAsync(ct));

        vehicles.MapGet("/{id:int}", async (int id, FleetDbContext db, CancellationToken ct) =>
            await db.Vehicles.FindAsync([id], ct) is { } v ? Results.Ok(v.ToDto()) : Results.NotFound());

        vehicles.MapPost("/", async (VehicleCreateDto dto, FleetDbContext db, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(dto.PlateNumber)) errors["plateNumber"] = ["Номер обязателен"];
            if (string.IsNullOrWhiteSpace(dto.Model)) errors["model"] = ["Модель обязательна"];
            if (dto.MileageKm < 0) errors["mileageKm"] = ["Пробег не может быть отрицательным"];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var plate = dto.PlateNumber.Trim().ToUpperInvariant();
            if (await db.Vehicles.AnyAsync(v => v.PlateNumber == plate, ct))
                return Results.Conflict(new { message = $"Машина {plate} уже есть" });

            var v = new Vehicle { PlateNumber = plate, Model = dto.Model.Trim(), MileageKm = dto.MileageKm };
            db.Vehicles.Add(v);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/vehicles/{v.Id}", v.ToDto());
        });

        vehicles.MapDelete("/{id:int}", async (int id, FleetDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Vehicles.Where(v => v.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        var requests = app.MapGroup("/api/requests").WithTags("Service requests");

        requests.MapGet("/", async (RequestStatus? status, bool? overdue, int? vehicleId,
            FleetDbContext db, CancellationToken ct) =>
        {
            var q = db.ServiceRequests.AsNoTracking().AsQueryable();
            if (status is not null) q = q.Where(r => r.Status == status);
            if (overdue is not null) q = q.Where(r => r.IsOverdue == overdue);
            if (vehicleId is not null) q = q.Where(r => r.VehicleId == vehicleId);
            var list = await q.ToListAsync(ct);
            // сначала срочные и с ближайшим сроком
            return list.OrderByDescending(r => r.Priority).ThenBy(r => r.DueAtUtc).Select(r => r.ToDto());
        });

        requests.MapGet("/{id:int}", async (int id, FleetDbContext db, CancellationToken ct) =>
            await db.ServiceRequests.FindAsync([id], ct) is { } r ? Results.Ok(r.ToDto()) : Results.NotFound());

        requests.MapPost("/", async (RequestCreateDto dto, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Description))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["description"] = ["Опишите проблему"] });
            if (!await db.Vehicles.AnyAsync(v => v.Id == dto.VehicleId, ct))
                return Results.NotFound(new { message = $"Машина {dto.VehicleId} не найдена" });

            var now = clock.GetUtcNow().UtcDateTime;
            var r = new ServiceRequest
            {
                VehicleId = dto.VehicleId,
                Description = dto.Description.Trim(),
                Priority = dto.Priority,
                CreatedAtUtc = now,
                DueAtUtc = dto.DueAtUtc?.ToUniversalTime() ?? DueDatePolicy.DefaultDue(dto.Priority, now)
            };
            db.ServiceRequests.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/requests/{r.Id}", r.ToDto());
        });

        requests.MapPut("/{id:int}/status", async (int id, RequestStatusDto dto, FleetDbContext db, CancellationToken ct) =>
        {
            var r = await db.ServiceRequests.FindAsync([id], ct);
            if (r is null) return Results.NotFound();
            if (!DueDatePolicy.CanMove(r.Status, dto.Status))
                return Results.Conflict(new { message = $"Нельзя перевести заявку из {r.Status} в {dto.Status}" });
            r.Status = dto.Status;
            if (dto.Status is RequestStatus.Done or RequestStatus.Cancelled) r.IsOverdue = false;
            await db.SaveChangesAsync(ct);
            return Results.Ok(r.ToDto());
        });

        app.MapGet("/api/stats", async (FleetDbContext db, CancellationToken ct) =>
        {
            var byStatus = await db.ServiceRequests.GroupBy(r => r.Status)
                .Select(g => new { status = g.Key.ToString(), count = g.Count() }).ToListAsync(ct);
            var overdue = await db.ServiceRequests.CountAsync(r => r.IsOverdue, ct);
            return Results.Ok(new { byStatus, overdue });
        }).WithTags("Stats");
    }
}
