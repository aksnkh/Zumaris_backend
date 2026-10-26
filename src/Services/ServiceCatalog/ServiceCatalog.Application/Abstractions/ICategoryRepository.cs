using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Application.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
}
