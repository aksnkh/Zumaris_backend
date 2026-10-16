using Identity.Application.Abstractions.Persistence;
using Identity.Domain.Entities;
using Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Tests;

// Exercises the real HTTP/authentication pipeline without connecting to any database.
public sealed class AuthFactory : WebApplicationFactory<Program>
{
    public string EnvironmentName { get; init; } = "Development";
    public const string TestKey = "test-only-signing-key-never-use-in-deployment-123456789";
    public MemoryUsers Users { get; } = new();
    public MemorySessions Sessions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestKey, ["Jwt:Issuer"] = "identity-tests", ["Jwt:Audience"] = "identity-test-clients",
            ["Jwt:AccessTokenExpirationMinutes"] = "15", ["Jwt:RefreshTokenExpirationDays"] = "7"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthRepository>();
            services.RemoveAll<ISessionRepository>();
            services.AddSingleton<IAuthRepository>(Users);
            services.AddSingleton<ISessionRepository>(Sessions);
        });
    }

    public HttpClient NewClient(bool csrf = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
            { HandleCookies = false, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (csrf) client.DefaultRequestHeaders.Add("X-Zumaris-CSRF", "1");
        return client;
    }
}

public sealed class MemoryUsers : IAuthRepository
{
    public User User { get; } = new()
    {
        Id = Guid.NewGuid(), UserName = "alice", Email = "alice@example.test", IsActive = true,
        PasswordHash = new PasswordHasher().Hash("Password-for-tests-1")
    };
    public Task<User?> GetUserByUsernameAsync(string userName, CancellationToken cancellationToken = default) =>
        Task.FromResult(userName == User.UserName ? User : null);
    public Task<bool> RegisterAsync(string username, string passwordHash, string email) => Task.FromResult(true);
}

// A contract fake, not a substitute for testing MySQL transactions after the manual migration.
public sealed class MemorySessions : ISessionRepository
{
    private readonly object gate = new();
    public Dictionary<Guid, AuthSession> Items { get; } = [];
    public Dictionary<string, (Guid SessionId, bool Used)> Tokens { get; } = [];

    public Task CreateAsync(AuthSession session, string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (gate) { Items.Add(session.Id, session); Tokens.Add(tokenHash, (session.Id, false)); }
        return Task.CompletedTask;
    }
    public Task<AuthSession?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(Tokens.TryGetValue(tokenHash, out var token) ? Items[token.SessionId] : null);
    }
    public Task<bool> RotateAsync(Guid sessionId, string oldHash, string newHash, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!Items.TryGetValue(sessionId, out var session) || session.RevokedAtUtc.HasValue ||
                session.ExpiresAtUtc <= DateTime.UtcNow || !Tokens.TryGetValue(oldHash, out var token) || token.SessionId != sessionId)
                return Task.FromResult(false);
            if (token.Used)
            {
                Items[sessionId] = session with { RevokedAtUtc = DateTime.UtcNow };
                return Task.FromResult(false);
            }
            Tokens[oldHash] = (sessionId, true);
            Tokens.Add(newHash, (sessionId, false));
            return Task.FromResult(true);
        }
    }
    public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(Items.TryGetValue(sessionId, out var s) && s.UserId == userId &&
            s.RevokedAtUtc is null && s.ExpiresAtUtc > DateTime.UtcNow);
    }
    public Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<AuthSession>>(Items.Values.Where(s => s.UserId == userId &&
            s.RevokedAtUtc is null && s.ExpiresAtUtc > DateTime.UtcNow).ToList());
    }
    public Task RevokeAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            if (Items.TryGetValue(sessionId, out var s) && s.UserId == userId)
                Items[sessionId] = s with { RevokedAtUtc = DateTime.UtcNow };
        return Task.CompletedTask;
    }
    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            foreach (var s in Items.Values.Where(s => s.UserId == userId).ToList())
                Items[s.Id] = s with { RevokedAtUtc = DateTime.UtcNow };
        return Task.CompletedTask;
    }
}
