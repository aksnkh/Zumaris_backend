using Identity.Application.Features.Auth;
using Microsoft.AspNetCore.DataProtection;
namespace Identity.Api.Security;
public sealed class RecoveryProtection(IDataProtectionProvider provider) : IRecoveryProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Zumaris.Identity.PasswordRecovery.Outbox.v1");
    public string Protect(string plain) => protector.Protect(plain);
    public string Unprotect(string encrypted) => protector.Unprotect(encrypted);
}
