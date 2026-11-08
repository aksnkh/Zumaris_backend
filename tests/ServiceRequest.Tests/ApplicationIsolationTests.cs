using System.Net;
using Xunit;
namespace ServiceRequest.Tests;
public sealed class ApplicationIsolationTests
{
    [Theory]
    [InlineData("App","GET","/api/technician/opportunities")]
    [InlineData("App","GET","/api/technician/jobs")]
    [InlineData("App","POST","/api/technician/jobs/11111111-1111-4111-8111-111111111111/start")]
    [InlineData("App","POST","/api/service-requests/11111111-1111-4111-8111-111111111111/quotes")]
    [InlineData("App","GET","/api/quotes/sent")]
    [InlineData("App","POST","/api/quotes/11111111-1111-4111-8111-111111111111/withdraw")]
    [InlineData("Tech","POST","/api/service-requests")]
    [InlineData("Tech","GET","/api/service-history")]
    [InlineData("Tech","GET","/api/service-requests")]
    [InlineData("Tech","GET","/api/quotes")]
    [InlineData("Tech","POST","/api/quotes/11111111-1111-4111-8111-111111111111/accept")]
    [InlineData("Tech","POST","/api/quotes/11111111-1111-4111-8111-111111111111/decline")]
    [InlineData("Admin","GET","/api/technician/jobs")]
    [InlineData("Admin","GET","/api/service-history")]
    public async Task Wrong_application_is_rejected_before_validation_or_repository_access(string application,string method,string path)
    {
        using var f=new RequestFactory();using var c=f.Client(application:application);
        var response=await c.SendAsync(new HttpRequestMessage(new(method),path));Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("This token is not authorized",await response.Content.ReadAsStringAsync());
    }
}
