namespace Identity.Application.Abstractions.Persistence;
public interface IPlatformAdminRepository
{
    Task<bool> IsAdminAsync(Guid userId, CancellationToken ct);
}
