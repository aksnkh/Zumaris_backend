using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Zumaris.ServiceCatalog.Application.Abstractions;
using Zumaris.ServiceCatalog.Domain.Entities;
using Xunit;

public class SimpleTokenTests
{
    private const string Key = "test-only-signing-key-at-least-32-bytes-long";

    [Theory]
    [InlineData("Testing",true,"1",200)]
    [InlineData("Development",true,"1",200)]
    [InlineData("Testing",false,"1",401)]
    [InlineData("Production",true,"1",200)]
    [InlineData("Production",false,"1",401)]
    [InlineData("Staging",true,"1",200)]
    [InlineData("Testing",true,"2",401)]
    [InlineData("Testing",true,null,401)]
    public async Task Simple_token_requires_explicit_opt_in_in_every_environment(
        string environment,bool enabled,string? token,int status)
    {
        using var factory = new Factory(environment,enabled);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new("Bearer",token);
        foreach (var path in new[] {"/api/areas","/api/categories"})
            Assert.Equal((HttpStatusCode)status,(await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(true,true,200)]
    [InlineData(true,false,401)]
    [InlineData(false,true,200)]
    public async Task Real_tokens_still_require_an_active_identity_session(bool enabled,bool active,int status)
    {
        using var factory = new Factory("Testing",enabled,active);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken("tests","tests",new[] {
            new Claim("sub",Guid.NewGuid().ToString()),new Claim("sid",Guid.NewGuid().ToString())
        },now.AddMinutes(-1),now.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer",new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal((HttpStatusCode)status,(await client.GetAsync("/api/categories")).StatusCode);
    }

    private class Factory(string environment,bool enabled,bool active = true) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["Testing:AllowSimpleToken"] = enabled.ToString(),
                ["Jwt:Key"] = Key,["Jwt:Issuer"]="tests",["Jwt:Audience"]="tests"
            }));
            builder.ConfigureServices(services => {
                services.RemoveAll<IAreaRepository>(); services.AddSingleton<IAreaRepository, Areas>();
                services.RemoveAll<ICategoryRepository>(); services.AddSingleton<ICategoryRepository, Categories>();
                services.RemoveAll<IIdentitySessionValidator>(); services.AddSingleton<IIdentitySessionValidator>(new Validator(active));
            });
        }
    }
    private class Areas : IAreaRepository
    {
        public Task<IReadOnlyList<Area>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Area>>(new[] {new Area(1,"Kitchen")});
    }
    private class Categories : ICategoryRepository
    {
        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Category>>(new[] {new Category(1,"Plumbing")});
    }
    private class Validator(bool active) : IIdentitySessionValidator
    {
        public Task<bool> IsActiveAsync(string accessToken,bool originalRequestIsHttps,CancellationToken ct) => Task.FromResult(active);
    }
}
