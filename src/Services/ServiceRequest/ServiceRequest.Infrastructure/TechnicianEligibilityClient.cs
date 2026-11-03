using System.Net;
using System.Net.Http.Json;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Infrastructure;
public sealed class TechnicianEligibilityClient(HttpClient client):ITechnicianEligibilityClient
{
 public async Task<TechnicianEligibility> GetAsync(Guid actor,string bearer,CancellationToken ct)
 {
  try
  {
   using var req=new HttpRequestMessage(HttpMethod.Get,"api/providers/me/eligibility");req.Headers.Authorization=new("Bearer",bearer);
   using var response=await client.SendAsync(req,ct);
   if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)throw new QuoteForbiddenException();
   if(!response.IsSuccessStatusCode)throw new QuoteUnavailableException("Provider eligibility validation is temporarily unavailable.");
   var p=await response.Content.ReadFromJsonAsync<TechnicianEligibility>(cancellationToken:ct);
   if(p is null||p.UserId!=actor||p.ProviderId==Guid.Empty||p.CategoryIds is null||p.CategoryIds.Count>100||p.CategoryIds.Any(x=>x<1))throw new System.Text.Json.JsonException();
   return p;
  }
  catch(Exception ex)when(ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException || ex is OperationCanceledException&&!ct.IsCancellationRequested)
  {throw new QuoteUnavailableException("Provider eligibility validation is temporarily unavailable.");}
 }
}
