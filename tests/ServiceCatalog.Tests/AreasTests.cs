using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumaris.ServiceCatalog.Application.Abstractions;
using Zumaris.ServiceCatalog.Domain.Entities;
using Xunit;

public class AreasTests
{
    [Fact]
    public async Task Read_only_contract_requires_authentication_and_returns_repository_data()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var rows = await client.GetFromJsonAsync<Area[]>("/api/areas");
        Assert.Equal(new[] { new Area(1,"Kitchen"), new Area(19,"Other") }, rows);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(new HttpRequestMessage(method,"/api/areas"))).StatusCode);
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/areas", swagger);
    }
    private class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["Jwt:Key"] = "test-only-signing-key-at-least-32-bytes-long", ["Jwt:Issuer"]="tests", ["Jwt:Audience"]="tests" }));
            builder.ConfigureServices(services => {
                services.RemoveAll<IAreaRepository>(); services.AddSingleton<IAreaRepository, Repository>();
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme="Test"; o.DefaultChallengeScheme="Test"; })
                    .AddScheme<AuthenticationSchemeOptions, Handler>("Test", _ => {});
            });
        }
    }
    private class Repository : IAreaRepository
    {
        public Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Area>>(new[] { new Area(1,"Kitchen"), new Area(19,"Other") });
    }
    private class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim("sub", Guid.NewGuid().ToString())},"Test")),"Test"))
                : AuthenticateResult.NoResult());
    }
}
