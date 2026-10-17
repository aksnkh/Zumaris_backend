namespace Zumaris.ServiceRequests.Domain;

public enum RequestUrgency { Standard, Priority, Emergency }
public enum RequestStatus { Submitted }

public sealed record ServiceRequestRecord(
    Guid RequestId, Guid UserId, Guid PropertyId, Guid CategoryId, Guid AreaTypeId,
    string Description, RequestUrgency Urgency, DateOnly? PreferredDate,
    TimeOnly? PreferredStartTime, TimeOnly? PreferredEndTime, string TimeZoneId,
    RequestStatus Status, Guid? TechnicianId, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, int Version);
