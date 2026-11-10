using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Zumaris.Provider.Application;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderSignupRequest([property:JsonRequired] bool AcceptTermsAndPrivacy);
public sealed record SignupIdentity(Guid UserId,string? FirstName,string? LastName,string? Phone);
public sealed record ProviderSignupResult(ProviderAccount Profile,DateTime TermsAndPrivacyAcceptedAtUtc,string? TermsVersion,string? PrivacyVersion);
public interface IProviderSignupClient
{
 Task<SignupIdentity> IdentityAsync(Guid actor,string bearer,CancellationToken ct);
}
public interface IProviderSignupRepository
{
 Task<ProviderSignupResult> CompleteAsync(Guid actor,string displayName,string? termsVersion,string? privacyVersion,CancellationToken ct);
}
public sealed class ProviderSignupService(IProviderSignupRepository repository,IProviderSignupClient client)
{
 public async Task<ProviderSignupResult> CompleteAsync(Guid actor,ProviderSignupRequest input,string bearer,string? termsVersion,string? privacyVersion,CancellationToken ct)
 {
  if(!input.AcceptTermsAndPrivacy)throw new ProviderWorkflowException(400,"You must agree to the Provider Terms and Privacy Policy.");
  var identity=await client.IdentityAsync(actor,bearer,ct);
  var display=ValidateIdentity(actor,identity);
  if(termsVersion?.Length>100||privacyVersion?.Length>100)throw new ProviderWorkflowException(503,"Provider terms configuration is unavailable.");
  return await repository.CompleteAsync(actor,display,termsVersion,privacyVersion,ct);
 }
 public static string ValidateIdentity(Guid actor,SignupIdentity identity)
 {
  if(identity.UserId!=actor)throw new ProviderWorkflowException(503,"Identity profile verification is temporarily unavailable.");
  if(string.IsNullOrWhiteSpace(identity.FirstName)||string.IsNullOrWhiteSpace(identity.LastName)||string.IsNullOrWhiteSpace(identity.Phone))
   throw new ProviderWorkflowException(400,"Complete your first name, last name and phone number before registering as a provider.");
  var display=identity.FirstName.Trim()+" "+identity.LastName.Trim();
  if(display.Length>200||display.Any(char.IsControl))throw new ProviderWorkflowException(400,"Your full name must not exceed 200 characters or contain control characters.");
  return display;
 }
}
