using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Application.Abstractions;

public interface IAreaRepository
{
    Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken);
}
