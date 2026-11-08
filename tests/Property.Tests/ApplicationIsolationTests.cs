using System.Net;
using Xunit;
namespace Property.Tests;
public class ApplicationIsolationTests
{
    [Theory][InlineData("Tech")][InlineData("Admin")]
    public async Task Non_customer_tokens_cannot_access_customer_property_data(string application)
    {
        using var f=new PropertyFactory();using var c=f.Client(application:application);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/properties")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsync("/api/properties",null)).StatusCode);
    }
}
