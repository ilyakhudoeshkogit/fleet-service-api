namespace FleetService.Api.Models;

public record VehicleCreateDto(string PlateNumber, string Model, int MileageKm);
public record VehicleDto(int Id, string PlateNumber, string Model, int MileageKm);

public record RequestCreateDto(int VehicleId, string Description, RequestPriority Priority, DateTime? DueAtUtc);
public record RequestStatusDto(RequestStatus Status);
public record RequestDto(int Id, int VehicleId, string Description, RequestPriority Priority,
    RequestStatus Status, DateTime CreatedAtUtc, DateTime DueAtUtc, bool IsOverdue);

public static class Mapping
{
    public static VehicleDto ToDto(this Vehicle v) => new(v.Id, v.PlateNumber, v.Model, v.MileageKm);
    public static RequestDto ToDto(this ServiceRequest r) =>
        new(r.Id, r.VehicleId, r.Description, r.Priority, r.Status, r.CreatedAtUtc, r.DueAtUtc, r.IsOverdue);
}
