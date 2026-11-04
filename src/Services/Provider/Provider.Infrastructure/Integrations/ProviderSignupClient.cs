using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Integrations;
public sealed class ProviderSignupClient(IHttpClientFactory clients):IProviderSignupClient
{
 private sealed record ProfileEnvelope(SignupIdentity? Payload);
 private static ProviderWorkflowException Unavailable()=>new(503,"Signup reference validation is temporarily unavailable.");
 private async Task<HttpResponseMessage> Send(string client,string path,string? bearer,CancellationToken ct)
 {
  using var request=new HttpRequestMessage(HttpMethod.Get,path);
  if(bearer is not null)request.Headers.Authorization=new("Bearer",bearer);
  try{return await clients.CreateClient(client).SendAsync(request,ct);}
  catch(Exception ex)when(ex is HttpRequestException||ex is OperationCanceledException&&!ct.IsCancellationRequested){throw Unavailable();}
 }
 public async Task<SignupIdentity> IdentityAsync(Guid actor,string bearer,CancellationToken ct)
 {
  using var response=await Send("IdentityAdmin","api/profile/me",bearer,ct);
  if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new ProviderWorkflowException(401,"Sign in again to complete provider registration.");
  if(response.StatusCode==HttpStatusCode.NotFound)throw new ProviderWorkflowException(400,"Complete your personal information before registering as a provider.");
  if(!response.IsSuccessStatusCode)throw Unavailable();
  try
  {
   var profile=(await response.Content.ReadFromJsonAsync<ProfileEnvelope>(cancellationToken:ct))?.Payload;
   if(profile is null||profile.UserId!=actor)throw Unavailable();return profile;
  }
  catch(Exception ex)when(ex is JsonException or NotSupportedException){throw Unavailable();}
 }
}
