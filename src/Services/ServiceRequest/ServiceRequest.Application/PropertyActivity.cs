namespace Zumaris.ServiceRequests.Application;
public sealed record PropertyServiceEvent(Guid EventId, Guid RequestId, Guid PropertyId, string RequestNumber, string Status, string? Reason, DateTime OccurredAtUtc);
public sealed record PropertyServiceSummary(long Total, long Open, long Upcoming, long Completed, long Cancelled);
public interface IPropertyActivityRepository
{
    Task<(IReadOnlyList<PropertyServiceEvent> Items, long Total)> Events(Guid actor, Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct);
    Task<PropertyServiceSummary> Summary(Guid actor, Guid property, CancellationToken ct);
}
