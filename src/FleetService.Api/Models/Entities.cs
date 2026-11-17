namespace FleetService.Api.Models;

public enum RequestStatus { New, InProgress, Done, Cancelled }
public enum RequestPriority { Low, Normal, High }

public class Vehicle
{
    public int Id { get; set; }
    public string PlateNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public int MileageKm { get; set; }
    public List<ServiceRequest> Requests { get; set; } = new();
}

public class ServiceRequest
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Description { get; set; } = "";
    public RequestPriority Priority { get; set; } = RequestPriority.Normal;
    public RequestStatus Status { get; set; } = RequestStatus.New;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DueAtUtc { get; set; }
    public bool IsOverdue { get; set; }
}
