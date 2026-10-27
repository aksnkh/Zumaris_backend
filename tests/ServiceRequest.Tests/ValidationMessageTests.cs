using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ServiceRequest.Tests;

public class ValidationMessageTests
{
    [Fact]
    public async Task Scheduling_validation_exposes_customer_message_and_preserves_field_errors()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var request = RequestFactory.Valid() with { PreferredDate = null, PreferredStartTime = null, PreferredEndTime = null };
        using var response = await client.PostAsJsonAsync("/api/service-requests",request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } });
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Provide the date, start and end together. Only Emergency may omit all three.",json.RootElement.GetProperty("message").GetString());
        Assert.Equal(400,json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("PreferredDate",out _));
        Assert.Empty(factory.Repository.Records);
    }

    [Fact]
    public async Task Invalid_json_has_a_safe_message_instead_of_parser_details()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        using var body = new StringContent("{broken",Encoding.UTF8,"application/json");
        using var response = await client.PostAsync("/api/service-requests",body);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
        Assert.True(json.RootElement.TryGetProperty("errors",out _));
        Assert.DoesNotContain("BytePositionInLine",text);
        Assert.DoesNotContain("JsonException",text);
        Assert.Empty(factory.Repository.Records);
    }

    [Fact]
    public async Task Handled_domain_validation_in_history_also_has_a_message()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        using var response = await client.GetAsync("/api/service-history?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("pageSize",json.RootElement.GetProperty("message").GetString());
        Assert.True(json.RootElement.TryGetProperty("errors",out _));
    }
}
