using FleetService.Api.Models;

namespace FleetService.Api.Services;

/// <summary>Срок выполнения заявки по приоритету, если клиент не указал его сам.</summary>
public static class DueDatePolicy
{
    public static DateTime DefaultDue(RequestPriority priority, DateTime nowUtc) => priority switch
    {
        RequestPriority.High => nowUtc.AddHours(4),
        RequestPriority.Normal => nowUtc.AddDays(1),
        _ => nowUtc.AddDays(3)
    };

    public static bool IsOverdue(ServiceRequest r, DateTime nowUtc) =>
        r.Status is RequestStatus.New or RequestStatus.InProgress && r.DueAtUtc < nowUtc;

    /// <summary>Разрешённые переходы статусов: New → InProgress → Done, отмена из New/InProgress.</summary>
    public static bool CanMove(RequestStatus from, RequestStatus to) => (from, to) switch
    {
        (RequestStatus.New, RequestStatus.InProgress) => true,
        (RequestStatus.InProgress, RequestStatus.Done) => true,
        (RequestStatus.New or RequestStatus.InProgress, RequestStatus.Cancelled) => true,
        _ => false
    };
}
