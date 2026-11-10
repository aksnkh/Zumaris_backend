using Identity.Application.Features.Signup;
using Identity.Application.Features.Auth;
using Microsoft.Extensions.Configuration;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Persistence;
// Legacy public signup is explicitly an App-only alias, never a shared identity registration.
public sealed class SignupRepository(IConfiguration configuration):ISignupRepository
{
 private readonly ApplicationAccountRepository accounts=new(configuration);
 public Task<User> CreateAsync(SignupAccount account,CancellationToken ct)=>accounts.CreateAsync("App",account,ct);
 public Task<User?> FindByEmailAsync(string email,CancellationToken ct)=>accounts.FindAsync("App",email,ct);
}
