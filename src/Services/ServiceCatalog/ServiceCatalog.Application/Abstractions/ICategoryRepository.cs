using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Application.Abstractions;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
}
