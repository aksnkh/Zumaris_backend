using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Identity.Tests;
public class ValidationMessageTests
{
    [Fact]
    public async Task Missing_login_fields_return_english_message_and_errors()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login",new {});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("Please provide a valid value",json.RootElement.GetProperty("message").GetString());
        Assert.True(json.RootElement.TryGetProperty("errors",out _));
    }
}
