using System.Net;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;

namespace ServiceRequest.Tests;

public sealed class IdentityValidatorTests
{
    [Theory]
    [InlineData(204, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    public async Task Sends_bearer_and_https_scheme_and_accepts_only_contract_status(int status, bool active)
    {
        using var handler = new StubHandler((HttpStatusCode)status);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5001/") };
        var validator = new IdentitySessionValidator(client);
        Assert.Equal(active, await validator.IsActiveAsync("test-token", true, default));
        Assert.Equal("/api/auth/session", handler.Path);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.Equal("https", handler.Scheme);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Unexpected_upstream_response_is_an_outage_not_an_active_session(int status)
    {
        using var client = new HttpClient(new StubHandler((HttpStatusCode)status)) { BaseAddress = new Uri("http://127.0.0.1:5001/") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new IdentitySessionValidator(client).IsActiveAsync("test-token", false, default));
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? Scheme { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Scheme = request.Headers.TryGetValues("X-Forwarded-Proto", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
