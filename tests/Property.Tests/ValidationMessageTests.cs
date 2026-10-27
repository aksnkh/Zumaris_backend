using System.Net;
using System.Text.Json;
using Xunit;
namespace Property.Tests;
public class ValidationMessageTests
{
    [Fact]
    public async Task Property_validation_has_message_and_original_field_errors()
    {
        using var factory = new PropertyFactory(); using var client = factory.Client();
        using var response = await client.GetAsync("/api/properties?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("pagination",out _));
    }
}
