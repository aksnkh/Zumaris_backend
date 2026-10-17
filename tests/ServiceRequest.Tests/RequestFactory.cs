using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;

namespace ServiceRequest.Tests;

public sealed class RequestFactory : WebApplicationFactory<Program>
{
    public const string Key = "test-only-service-request-key-do-not-use-in-production-1234";
    public Guid UserId { get; } = Guid.NewGuid();
    public MemoryRequests Repository { get; } = new();
    public SessionValidator Validator { get; } = new();
    public TestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "request-tests", ["Jwt:Audience"] = "request-clients" }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceRequestRepository>(); services.RemoveAll<IIdentitySessionValidator>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<IServiceRequestRepository>(Repository);
            services.AddSingleton<IIdentitySessionValidator>(Validator);
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Client(bool authenticated = true, string? signingKey = null)
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        if (authenticated)
        {
            var token = new JwtSecurityToken("request-tests", "request-clients",
                [new Claim("sub", UserId.ToString()), new Claim("sid", Guid.NewGuid().ToString())],
                DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }

    public static CreateServiceRequest Valid() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "The kitchen tap is leaking and needs repair.", RequestUrgency.Standard,
        new DateOnly(2030, 1, 2), new TimeOnly(10, 0), new TimeOnly(12, 0));
}

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class SessionValidator : IIdentitySessionValidator
{
    public bool Active { get; set; } = true;
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }
    public Task<bool> IsActiveAsync(string token, bool originalRequestIsHttps, CancellationToken ct)
    {
        Calls++;
        if (Unavailable) throw new HttpRequestException("test upstream unavailable");
        return Task.FromResult(Active);
    }
}

// Contract fake only; real transaction and unique-index concurrency must be validated on MySQL.
public sealed class MemoryRequests : IServiceRequestRepository
{
    private readonly object gate = new();
    public List<PersistedRequest> Records { get; } = [];
    private readonly Dictionary<(Guid, string), PersistedRequest> keys = [];
    public Task<PersistedRequest?> FindByKeyAsync(Guid userId, string key, CancellationToken ct)
    { lock (gate) return Task.FromResult(keys.GetValueOrDefault((userId, key))); }
    public Task<CreateResult> CreateAsync(ServiceRequestRecord request, string? key, string hash, CancellationToken ct)
    {
        lock (gate)
        {
            if (key is not null && keys.TryGetValue((request.UserId, key), out var existing))
            {
                if (existing.RequestHash != hash) throw new IdempotencyConflictException();
                return Task.FromResult(new CreateResult(ServiceRequestService.ToCreated(existing.Request), true));
            }
            var persisted = new PersistedRequest(request, hash);
            Records.Add(persisted);
            if (key is not null) keys.Add((request.UserId, key), persisted);
            return Task.FromResult(new CreateResult(ServiceRequestService.ToCreated(request), false));
        }
    }
}
