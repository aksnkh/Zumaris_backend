using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Microsoft.IdentityModel.Tokens;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Security;
public sealed class ProviderEnrollmentClient(HttpClient client,JwtSettings settings):IProviderEnrollmentClient
{
 private sealed record ProviderProfile(Guid ProviderId,Guid UserId,bool IsActive);
 private sealed record SignupResult(ProviderProfile Profile);
 private sealed record Envelope(SignupResult? Payload);
 public async Task<ProviderLoginAccess> EnrollAsync(User user,CancellationToken ct)
 {
  if(user.Application!="Tech"||user.ProviderEnrollmentRequestedAtUtc is null)throw new ApplicationLoginException(403,"Tech registration and consent are required.");
  var now=DateTime.UtcNow;var jwt=new JwtSecurityToken(settings.Issuer,"Zumaris.Provider.LoginAccess",
   [new("sub",user.Id.ToString()),new("scope","provider-enrollment"),new("application","Tech"),new("jti",Guid.NewGuid().ToString())],now,now.AddSeconds(30),
   new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),SecurityAlgorithms.HmacSha256));
  using var request=new HttpRequestMessage(HttpMethod.Post,$"api/internal/users/{user.Id}/signup");
  request.Headers.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(jwt));
  request.Content=JsonContent.Create(new{user.FirstName,user.LastName,user.Phone,acceptTermsAndPrivacy=true});
  try
  {
   using var response=await client.SendAsync(request,ct);
   if(!response.IsSuccessStatusCode)throw Unavailable();
   var profile=(await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken:ct))?.Payload?.Profile;
   if(profile is null||profile.UserId!=user.Id||profile.ProviderId==Guid.Empty)throw Unavailable();
   return new(user.Id,profile.ProviderId,profile.IsActive,true);
  }
  catch(Exception ex)when(ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException||ex is OperationCanceledException&&!ct.IsCancellationRequested){throw Unavailable();}
 }
 private static ApplicationLoginException Unavailable()=>new(503,"Tech provider setup is temporarily unavailable. Sign in to Tech to retry.");
}
