using System.Net;
using System.Net.Http.Json;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Integrations;
public sealed class ProviderWorkflowClient(IHttpClientFactory clients) : IProviderWorkflowClient
{
    private async Task<HttpResponseMessage> Send(string client,string path,string bearer,CancellationToken ct)
    {
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",bearer);
            return await clients.CreateClient(client).SendAsync(request,ct);
        }
        catch(Exception ex)when(ex is HttpRequestException || ex is OperationCanceledException&&!ct.IsCancellationRequested)
        {throw new ProviderWorkflowException(503,"The reference service is temporarily unavailable.");}
    }
    public async Task<bool> IsAdminAsync(string bearer,CancellationToken ct)
    {
        using var response=await Send("IdentityAdmin","api/auth/session/admin-access",bearer,ct);
        if(response.StatusCode==HttpStatusCode.NoContent)return true;
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)return false;
        throw new ProviderWorkflowException(503,"Administrator verification is temporarily unavailable.");
    }
    public async Task<CompletedWork> CompletedWorkAsync(Guid id,string bearer,CancellationToken ct)
    {
        using var response=await Send("ServiceRequest","api/service-requests/"+id+"/completed-work",bearer,ct);
        if(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)throw new ProviderWorkflowException(404,"Completed job not found.");
        if(response.StatusCode==HttpStatusCode.Conflict)throw new ProviderWorkflowException(409,"The job must be completed before submitting a review.");
        if(!response.IsSuccessStatusCode)throw new ProviderWorkflowException(503,"Completed job verification is temporarily unavailable.");
        try
        {
            var work=await response.Content.ReadFromJsonAsync<CompletedWork>(cancellationToken:ct);
            if(work is null||work.RequestId!=id||work.ProviderId==Guid.Empty||work.Status!="Completed")throw new System.Text.Json.JsonException();
            return work;
        }
        catch(Exception ex)when(ex is System.Text.Json.JsonException or NotSupportedException){throw new ProviderWorkflowException(503,"Completed job verification is temporarily unavailable.");}
    }
    private sealed record Stats(int CompletedJobs);
    public async Task<int?> CompletedJobsAsync(Guid id,string bearer,CancellationToken ct)
    {
        try
        {
            using var response=await Send("ServiceRequest",$"api/technician/providers/{id}/work-stats",bearer,ct);
            if(!response.IsSuccessStatusCode)return null;
            var stats=await response.Content.ReadFromJsonAsync<Stats>(cancellationToken:ct);
            return stats?.CompletedJobs>=0?stats.CompletedJobs:null;
        }
        catch(Exception ex)when(ex is ProviderWorkflowException or System.Text.Json.JsonException or NotSupportedException || ex is OperationCanceledException&&!ct.IsCancellationRequested){return null;}
    }
}
