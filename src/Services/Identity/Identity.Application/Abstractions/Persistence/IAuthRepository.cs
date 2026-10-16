using Identity.Domain.Entities;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Abstractions.Persistence;

public interface IAuthRepository
{
    Task<User?> GetUserByUsernameAsync(string userName, CancellationToken cancellationToken = default);

    Task<bool> RegisterAsync(string username, string passwordHash, string email);
}
