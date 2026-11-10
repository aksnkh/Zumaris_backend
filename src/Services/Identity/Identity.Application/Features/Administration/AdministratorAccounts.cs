using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Signup;

namespace Identity.Application.Features.Administration;

public sealed record AdministratorAccountResponse(Guid UserId, string UserName, string Email,
    string FirstName, string LastName, string Phone, string Role, DateTime CreatedAtUtc, Guid? CreatedByUserId);

public interface IAdministratorAccountRepository
{
    // Null actor is reserved for the server-key-protected, one-time bootstrap route.
    Task<AdministratorAccountResponse> CreateAsync(SignupAccount account, Guid? actorUserId, DateTime now, CancellationToken ct);
}

public sealed class AdministratorAccountService(IAdministratorAccountRepository repository, IPasswordHasher passwords, TimeProvider clock)
{
    public Task<AdministratorAccountResponse> CreateAsync(SignupRequest input, Guid? actorUserId, CancellationToken ct)
    {
        var account = SignupValidation.ValidateAndHash(input, passwords);
        return repository.CreateAsync(account, actorUserId, clock.GetUtcNow().UtcDateTime, ct);
    }
}
