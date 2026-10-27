using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;

namespace ServiceRequest.Tests;

public sealed class CreateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static Task<HttpResponseMessage> Post(HttpClient client, CreateServiceRequest request) =>
        client.PostAsJsonAsync("/api/service-requests", request, Json);

    [Fact]
    public async Task Creates_submitted_request_using_authenticated_user_and_returns_201()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var input = RequestFactory.Valid();
        using var response = await Post(client, input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = Assert.Single(factory.Repository.Records).Request;
        Assert.Equal(factory.UserId, saved.UserId); Assert.Equal(input.AreaTypeId, saved.AreaTypeId);
        Assert.Equal(RequestStatus.Submitted, saved.Status); Assert.Null(saved.TechnicianId);
        Assert.Equal(1, saved.Version); Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(saved.RequestId.ToString(), json!["payload"]!["requestId"]!.GetValue<string>());
        Assert.Equal("Submitted", json["payload"]!["status"]!.GetValue<string>());
        var location = response.Headers.Location!;
        Assert.Equal($"https://api.zumaris.com/service-request/api/service-requests/{saved.RequestId}",
            new Uri(new Uri("https://api.zumaris.com/service-request/api/service-requests"), location).ToString());
    }

    [Theory]
    [InlineData("categoryId", "-1")]
    [InlineData("areaTypeId", "-1")]
    [InlineData("categoryId", "\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"")]
    [InlineData("areaTypeId", "\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"")]
    public async Task Catalog_ids_reject_negative_numbers_and_legacy_guids(string field, string value)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var body = JsonSerializer.SerializeToNode(RequestFactory.Valid(), Json)!.AsObject();
        body[field] = JsonNode.Parse(value);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/service-requests",body)).StatusCode);
        Assert.Empty(factory.Repository.Records);
    }

    [Theory]
    [InlineData(19, HttpStatusCode.BadRequest)]
    [InlineData(20, HttpStatusCode.Created)]
    [InlineData(500, HttpStatusCode.Created)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task Enforces_description_boundaries(int length, HttpStatusCode expected)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        Assert.Equal(expected, (await Post(client, RequestFactory.Valid() with { Description = new string('x', length) })).StatusCode);
        if (expected == HttpStatusCode.BadRequest) Assert.Empty(factory.Repository.Records);
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("technicianId")]
    [InlineData("status")]
    [InlineData("photoIds")]
    public async Task Rejects_client_owned_server_fields(string field)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var body = JsonSerializer.SerializeToNode(RequestFactory.Valid(), Json)!.AsObject();
        body[field] = "not-allowed";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/service-requests", body)).StatusCode);
        Assert.Empty(factory.Repository.Records);
    }

    [Theory]
    [InlineData(10, HttpStatusCode.BadRequest)]
    [InlineData(20, HttpStatusCode.Created)]
    public async Task Counts_unicode_code_points_consistently_with_mysql(int count, HttpStatusCode expected)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var input = RequestFactory.Valid() with { Description = string.Concat(Enumerable.Repeat("🔧", count)) };
        Assert.Equal(expected, (await Post(client, input)).StatusCode);
    }

    [Theory]
    [InlineData("propertyId")]
    [InlineData("categoryId")]
    [InlineData("areaTypeId")]
    public async Task Rejects_empty_reference_ids(string field)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var body = JsonSerializer.SerializeToNode(RequestFactory.Valid(), Json)!.AsObject(); body[field] = field == "propertyId" ? JsonValue.Create(Guid.Empty) : JsonValue.Create(0);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/service-requests", body)).StatusCode);
    }

    [Fact]
    public async Task Requires_authentication_and_valid_signature_before_session_lookup()
    {
        using var factory = new RequestFactory(); using var anonymous = factory.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, RequestFactory.Valid())).StatusCode);
        using var forged = factory.Client(signingKey: new string('z', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(forged, RequestFactory.Valid())).StatusCode);
        Assert.Equal(0, factory.Validator.Calls); Assert.Empty(factory.Repository.Records);
    }

    [Fact]
    public async Task Revoked_session_is_rejected_and_identity_outage_fails_closed()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        factory.Validator.Active = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, RequestFactory.Valid())).StatusCode);
        factory.Validator.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(client, RequestFactory.Valid())).StatusCode);
        Assert.Empty(factory.Repository.Records);
    }

    [Fact]
    public async Task Emergency_can_omit_schedule_but_standard_cannot()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var request = RequestFactory.Valid() with { PreferredDate = null, PreferredStartTime = null, PreferredEndTime = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(client, request with { Urgency = RequestUrgency.Emergency })).StatusCode);
    }

    [Theory]
    [InlineData("past")]
    [InlineData("reversed")]
    [InlineData("partial")]
    [InlineData("fractional")]
    public async Task Rejects_invalid_schedule(string problem)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var request = RequestFactory.Valid();
        request = problem switch
        {
            "past" => request with { PreferredDate = new DateOnly(2029, 12, 31) },
            "reversed" => request with { PreferredEndTime = new TimeOnly(9, 0) },
            "partial" => request with { PreferredEndTime = null },
            _ => request with { PreferredStartTime = new TimeOnly(10, 0, 0, 100) }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, request)).StatusCode);
        Assert.Empty(factory.Repository.Records);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"Invalid\"")]
    public async Task Rejects_numeric_and_unknown_urgency(string value)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        var body = JsonSerializer.SerializeToNode(RequestFactory.Valid(), Json)!.AsObject(); body["urgency"] = JsonNode.Parse(value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/service-requests", body)).StatusCode);
    }

    [Fact]
    public async Task Idempotent_retry_preserves_id_even_after_preferred_time_and_conflicts_on_changed_body()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "request-test-0001"); var request = RequestFactory.Valid();
        using var first = await Post(client, request); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        factory.Clock.Now = factory.Clock.Now.AddDays(7);
        using var retry = await Post(client, request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.Headers.Location, retry.Headers.Location); Assert.True(retry.Headers.Contains("Idempotency-Replayed"));
        Assert.Single(factory.Repository.Records);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, request with { Description = "A different kitchen repair request description." })).StatusCode);
    }

    [Fact]
    public async Task Parallel_retries_create_only_one_request_in_contract_fake()
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "request-parallel-0001"); var request = RequestFactory.Valid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(client, request)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(factory.Repository.Records); Assert.Single(responses.Select(r => r.Headers.Location).Distinct());
    }

    [Fact]
    public async Task Same_idempotency_key_is_independent_for_different_users()
    {
        var repository = new MemoryRequests(); var clock = new TestClock();
        var service = new ServiceRequestService(repository, clock, new()); var request = RequestFactory.Valid();
        var first = await service.CreateAsync(Guid.NewGuid(), request, "same-key-0001");
        var second = await service.CreateAsync(Guid.NewGuid(), request, "same-key-0001");
        Assert.NotEqual(first.Request.RequestId, second.Request.RequestId); Assert.Equal(2, repository.Records.Count);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("bad key with spaces")]
    public async Task Rejects_invalid_idempotency_key_without_writing(string key)
    {
        using var factory = new RequestFactory(); using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, RequestFactory.Valid())).StatusCode);
        Assert.Empty(factory.Repository.Records);
    }

    [Fact]
    public async Task Swagger_is_compatible_with_gateway_prefix()
    {
        using var factory = new RequestFactory(); using var client = factory.Client(false);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/api/service-requests", out _));
        var properties = json.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("CreateServiceRequest").GetProperty("properties");
        Assert.Equal("integer",properties.GetProperty("categoryId").GetProperty("type").GetString());
        Assert.Equal("integer",properties.GetProperty("areaTypeId").GetProperty("type").GetString());
        Assert.Equal("uuid",properties.GetProperty("propertyId").GetProperty("format").GetString());
        Assert.Contains(json.RootElement.GetProperty("paths").GetProperty("/api/service-requests").GetProperty("post").GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
        var server = json.RootElement.GetProperty("servers")[0].GetProperty("url").GetString();
        Assert.Equal("https://api.zumaris.com/service-request/", new Uri(new Uri("https://api.zumaris.com/service-request/swagger/v1/swagger.json"), server!).ToString());
    }
}
