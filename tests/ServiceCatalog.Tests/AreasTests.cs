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
using Zumaris.ServiceCatalog.Api.Controllers;

public class AreasTests
{
    [Fact]
    public async Task Read_only_contract_requires_authentication_and_returns_repository_data()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var rows = await client.GetFromJsonAsync<AreaResponse[]>("/api/areas");
        Assert.Equal(new[] { new AreaResponse(1,"Kitchen","/service-catalog/api/areas/icons/1.png"), new AreaResponse(19,"Other",null) }, rows);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(new HttpRequestMessage(method,"/api/areas"))).StatusCode);
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/areas", swagger);
    }
    [Fact]
    public async Task Icons_are_public_and_only_serve_supported_files_from_the_configured_directory()
    {
        using var factory = new Factory();
        Directory.CreateDirectory(factory.IconDirectory);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(factory.IconDirectory,"1.png"),bytes);
            await File.WriteAllTextAsync(Path.Combine(factory.IconDirectory,"secret.txt"),"private");
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/areas/icons/1.png");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("nosniff",response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/areas/icons/secret.txt")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/areas/icons/missing.png")).StatusCode);
        }
        finally { Directory.Delete(factory.IconDirectory,true); }
    }
    [Theory]
    [InlineData("../secret.png")]
    [InlineData("C:\\secret.png")]
    [InlineData("file.html")]
    [InlineData("/tmp/icon.png")]
    public void Icon_names_cannot_reference_paths_or_non_image_files(string name) =>
        Assert.False(CatalogIconFiles.IsValidName(name));
    private class Factory : WebApplicationFactory<Program>
    {
        public string IconDirectory { get; } = Path.Combine(Path.GetTempPath(),"area-icons-" + Guid.NewGuid());
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["AreaIcons:Directory"] = IconDirectory, ["Jwt:Key"] = "test-only-signing-key-at-least-32-bytes-long", ["Jwt:Issuer"]="tests", ["Jwt:Audience"]="tests" }));
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
            Task.FromResult<IReadOnlyList<Area>>(new[] { new Area(1,"Kitchen","1.png"), new Area(19,"Other") });
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
