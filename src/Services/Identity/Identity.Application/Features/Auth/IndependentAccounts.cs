using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Identity.Application.Features.Signup;
using Identity.Application.Features.Auth.Models;
using Identity.Application.Abstractions.Security;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Application.Features.Auth;

public interface IApplicationAccountRepository
{
 Task<User?> FindAsync(string application,string login,CancellationToken ct);
 Task<User?> FindByIdAsync(string application,Guid id,CancellationToken ct);
 Task<User> CreateAsync(string application,SignupAccount account,CancellationToken ct);
}
public interface IProviderEnrollmentClient
{
 Task<ProviderLoginAccess> EnrollAsync(User technician,CancellationToken ct);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountLoginRequest([Required,StringLength(255)]string UserName,[Required,StringLength(1024)]string Password);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TechnicianRegisterRequest([Required,StringLength(100)]string FirstName,[Required,StringLength(100)]string LastName,
 [Required,StringLength(254)]string Email,[Required,StringLength(40)]string Phone,[Required,StringLength(1024,MinimumLength=8)]string Password,
 [property:JsonRequired]bool AcceptTermsAndPrivacy);
public sealed class IndependentRegistrationService(IApplicationAccountRepository accounts,IPasswordHasher passwords,
 ApplicationLoginAccess access,IUserSessionIssuer sessions)
{
 public async Task<(AuthResponse Response,string RefreshToken)> RegisterTechAsync(TechnicianRegisterRequest input,CancellationToken ct)
 {
  if(!input.AcceptTermsAndPrivacy)throw new SignupException(400,"You must agree to the Provider Terms and Privacy Policy.");
  var account=SignupValidation.ValidateAndHash(new(input.FirstName,input.LastName,input.Email,input.Phone,input.Password),passwords);
  var user=await accounts.CreateAsync("Tech",account,ct);
  try
  {
   var grant=await access.ResolveAsync(user.Id,"Tech",ct);
   return await sessions.IssueAsync(user,ct,"Tech",grant.ProviderId);
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
  catch{throw new SignupException(503,"Your Tech account was created, but provider setup or sign-in is temporarily unavailable. Sign in to Tech to retry.");}
 }
}
