using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zumaris.ServiceRequests.Application.Quotes;
using Xunit;
namespace ServiceRequest.Tests;
public class QuoteTests
{
    [Theory]
    [InlineData("GET","/api/quotes")]
    [InlineData("GET","/api/quotes/sent")]
    [InlineData("GET","/api/quotes/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST","/api/quotes/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST","/api/quotes/00000000-0000-0000-0000-000000000001/decline")]
    [InlineData("POST","/api/quotes/00000000-0000-0000-0000-000000000001/withdraw")]
    [InlineData("POST","/api/service-requests/00000000-0000-0000-0000-000000000001/quotes")]
    public async Task Quote_endpoints_require_authentication(string method,string path)
    {
        using var f=new RequestFactory();using var c=f.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.SendAsync(new HttpRequestMessage(new(method),path))).StatusCode);
    }
    [Theory]
    [InlineData(-1,20,"GBP")]
    [InlineData(0,0,"GBP")]
    [InlineData(75.001,20,"GBP")]
    [InlineData(75,20,"USD")]
    public async Task Invalid_quote_prices_have_customer_messages(double labour,double callout,string currency)
    {
        using var f=new RequestFactory();using var c=f.Client(application:"Tech");
        using var response=await c.PostAsJsonAsync($"/api/service-requests/{Guid.NewGuid()}/quotes",new{labourAmount=labour,calloutAmount=callout,currencyCode=currency});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
    }
    [Fact]
    public async Task Huge_decimal_inputs_do_not_overflow_and_provider_identity_cannot_be_spoofed()
    {
        using var f=new RequestFactory();using var c=f.Client(application:"Tech");
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/service-requests/{Guid.NewGuid()}/quotes",new{labourAmount=decimal.MaxValue,calloutAmount=decimal.MaxValue})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/service-requests/{Guid.NewGuid()}/quotes",new{labourAmount=75,calloutAmount=20,providerId=Guid.NewGuid(),totalAmount=1})).StatusCode);
    }
    [Theory]
    [InlineData("status=Unknown")]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=101")]
    public async Task Invalid_list_filters_do_not_reach_database(string query)
    {
        using var f=new RequestFactory();using var c=f.Client();
        Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/api/quotes?"+query)).StatusCode);
    }
}
