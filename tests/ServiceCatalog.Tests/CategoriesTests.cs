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

public class CategoriesTests
{
    [Fact]
    public async Task Get_by_id_requires_auth_and_returns_one_category_or_404()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/categories/1")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User","yes");
        Assert.Equal(new CategoryResponse(1,"Plumbing","/service-catalog/api/categories/icons/1.png"),
            await client.GetFromJsonAsync<CategoryResponse>("/api/categories/1"));
        Assert.Equal(new CategoryResponse(17,"Other",null),
            await client.GetFromJsonAsync<CategoryResponse>("/api/categories/17"));
        foreach (var id in new[] {"999","0","-1","invalid"})
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/categories/"+id)).StatusCode);
    }
    [Fact]
    public async Task Read_only_contract_requires_authentication_and_returns_repository_data()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "yes");
        var rows = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories");
        Assert.Equal(new[] { new CategoryResponse(1,"Plumbing","/service-catalog/api/categories/icons/1.png"), new CategoryResponse(17,"Other",null) }, rows);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(new HttpRequestMessage(method,"/api/categories"))).StatusCode);
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/categories", swagger);
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
            using var response = await client.GetAsync("/api/categories/icons/1.png");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("nosniff",response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/categories/icons/secret.txt")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/categories/icons/missing.png")).StatusCode);
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
        public string IconDirectory { get; } = Path.Combine(Path.GetTempPath(),"category-icons-" + Guid.NewGuid());
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["CategoryIcons:Directory"] = IconDirectory, ["Jwt:Key"] = "test-only-signing-key-at-least-32-bytes-long", ["Jwt:Issuer"]="tests", ["Jwt:Audience"]="tests" }));
            builder.ConfigureServices(services => {
                services.RemoveAll<ICategoryRepository>(); services.AddSingleton<ICategoryRepository, Repository>();
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme="Test"; o.DefaultChallengeScheme="Test"; })
                    .AddScheme<AuthenticationSchemeOptions, Handler>("Test", _ => {});
            });
        }
    }
    private class Repository : ICategoryRepository
    {
        public async Task<Category?> GetByIdAsync(int id, CancellationToken ct) =>
            (await ListAsync(ct)).SingleOrDefault(category => category.Id == id);
        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Category>>(new[] { new Category(1,"Plumbing","1.png"), new Category(17,"Other") });
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
