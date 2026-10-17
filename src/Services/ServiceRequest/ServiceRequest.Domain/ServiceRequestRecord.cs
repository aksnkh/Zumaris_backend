namespace Zumaris.ServiceRequests.Domain;

public enum RequestUrgency { Standard, Priority, Emergency }
public enum RequestStatus { Submitted, Assigned, InProgress, Completed, Cancelled }

public static class RequestStateRules
{
    public static bool CanEdit(RequestStatus status) => status == RequestStatus.Submitted;
    public static bool CanCancel(RequestStatus status) => status is RequestStatus.Submitted or RequestStatus.Assigned;
    public static bool CanAddPhotos(RequestStatus status) => status is RequestStatus.Submitted or RequestStatus.Assigned;
}

public sealed record ServiceRequestRecord(
    Guid RequestId, Guid UserId, Guid PropertyId, Guid CategoryId, Guid AreaTypeId,
    string Description, RequestUrgency Urgency, DateOnly? PreferredDate,
    TimeOnly? PreferredStartTime, TimeOnly? PreferredEndTime, string TimeZoneId,
    RequestStatus Status, Guid? TechnicianId, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, int Version);
