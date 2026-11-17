namespace Zumaris.ServiceRequests.Application;
public sealed record JobContactSnapshot(string? PropertyAddress, decimal? Latitude, decimal? Longitude, string? CustomerName, string? CustomerPhone);
public interface IJobContactLookup
{
    Task<JobContactSnapshot> Get(Guid customerId, Guid propertyId, string bearer, CancellationToken ct);
}
public sealed class JobContactUnavailableException : Exception
{
    public JobContactUnavailableException() : base("Job contact information is temporarily unavailable.") { }
}
