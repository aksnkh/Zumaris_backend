using System.Net;
using System.Net.Http.Json;
using Zumaris.Billing.Application;
namespace Zumaris.Billing.Infrastructure;
public sealed class CompletedWorkClient(HttpClient client):ICompletedWorkClient
{
 public async Task<CompletedWork> GetAsync(Guid actor,Guid id,string bearer,CancellationToken ct)
 {
  try
  {
   using var req=new HttpRequestMessage(HttpMethod.Get,$"api/service-requests/{id}/completed-work");req.Headers.Authorization=new("Bearer",bearer);
   using var response=await client.SendAsync(req,ct);
   if(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)throw new BillingException(404,"Completed job not found.");
   if(response.StatusCode==HttpStatusCode.Conflict)throw new BillingException(409,"The job must be completed before requesting payment.");
   if(!response.IsSuccessStatusCode)throw new BillingException(503,"Completed job verification is temporarily unavailable.");
   var work=await response.Content.ReadFromJsonAsync<CompletedWork>(cancellationToken:ct);
   if(work is null||work.RequestId!=id||work.CustomerId!=actor)throw new System.Text.Json.JsonException();return work;
  }
  catch(Exception ex)when(ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException||ex is OperationCanceledException&&!ct.IsCancellationRequested)
  {throw new BillingException(503,"Completed job verification is temporarily unavailable.");}
 }
}
