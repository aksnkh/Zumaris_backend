using Identity.Application.Features.Signup;
using Identity.Application.Features.Profile;
using Identity.Application.Abstractions.Persistence;
using Identity.Application.Features.Auth;
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
    public FakeApplicationProviders ApplicationProviders { get; } = new();
    public FakeApplicationAdmins ApplicationAdmins { get; } = new();
    public MemoryUsers Users { get; } = new();
    public MemorySessions Sessions { get; } = new();
    public FakeGoogleValidator Google { get; } = new();
    public FakeExternalLogins External { get; }
    public MemoryProfiles Profiles { get; }
    public string ProfilePhotoRoot { get; } = Path.Combine(Path.GetTempPath(),"zumaris-profile-tests-"+Guid.NewGuid().ToString("N"));
    public MemorySignup Signups {get;}
    public AuthFactory() { External = new FakeExternalLogins(Users); Profiles = new MemoryProfiles(Users); Signups=new MemorySignup(Users,Profiles); }
    public const string BffKey = "test-only-bff-key-never-deploy-this-123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestKey, ["Jwt:Issuer"] = "identity-tests", ["Jwt:Audience"] = "identity-test-clients",
            ["Jwt:AccessTokenExpirationMinutes"] = "15", ["Jwt:RefreshTokenExpirationDays"] = "7", ["Bff:ClientKey"] = BffKey
        }));
        builder.ConfigureAppConfiguration((_,config) => config.AddInMemoryCollection(new Dictionary<string,string?> { ["ProfilePhotos:RootPath"]=ProfilePhotoRoot }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProviderLoginAccessClient>();services.AddSingleton<IProviderLoginAccessClient>(ApplicationProviders);
            services.RemoveAll<IPlatformAdminRepository>();services.AddSingleton<IPlatformAdminRepository>(ApplicationAdmins);
            services.RemoveAll<ISignupRepository>();services.AddSingleton<ISignupRepository>(Signups);
            services.RemoveAll<IProfileRepository>();
            services.AddSingleton<IProfileRepository>(Profiles);
            services.RemoveAll<IAuthRepository>();
            services.RemoveAll<ISessionRepository>();
            services.RemoveAll<IGoogleIdentityValidator>();
            services.RemoveAll<IExternalLoginRepository>();
            services.AddSingleton<IAuthRepository>(Users);
            services.AddSingleton<ISessionRepository>(Sessions);
            services.AddSingleton<IGoogleIdentityValidator>(Google);
            services.AddSingleton<IExternalLoginRepository>(External);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ProfilePhotoRoot)) Directory.Delete(ProfilePhotoRoot,true);
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
    public List<User> GoogleUsers { get; } = [];
    public User User { get; } = new()
    {
        Id = Guid.NewGuid(), UserName = "alice", Email = "alice@example.test", IsActive = true,
        PasswordHash = new PasswordHasher().Hash("Password-for-tests-1")
    };
    public Task<User?> GetUserByUsernameAsync(string userName, CancellationToken cancellationToken = default) =>
        Task.FromResult(userName == User.UserName ? User : GoogleUsers.SingleOrDefault(u => u.UserName == userName));
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


public sealed class FakeGoogleValidator : IGoogleIdentityValidator
{
    public VerifiedGoogleIdentity Identity { get; set; } = new("google-subject-test-1", "google@example.test");
    public bool Reject { get; set; }
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }
    public Task<VerifiedGoogleIdentity> ValidateAsync(string idToken, CancellationToken ct)
    {
        Calls++;
        if (Unavailable) throw new GoogleLoginUnavailableException();
        if (Reject) throw new UnauthorizedAccessException();
        return Task.FromResult(Identity);
    }
}
public sealed class FakeExternalLogins(MemoryUsers users) : IExternalLoginRepository
{
    private readonly Dictionary<string, User> links = [];
    private readonly object gate = new();
    public Task<User> FindOrCreateGoogleAsync(VerifiedGoogleIdentity identity, CancellationToken ct)
    {
        lock (gate)
        {
            if (links.TryGetValue(identity.Subject, out var existing)) return Task.FromResult(existing);
            if (new[] { users.User }.Concat(users.GoogleUsers).Any(u => string.Equals(u.Email, identity.Email, StringComparison.OrdinalIgnoreCase)))
                throw new ExternalLoginConflictException();
            var user = new User { Id = Guid.NewGuid(), UserName = "google_" + Guid.NewGuid().ToString("N"), Email = identity.Email, PasswordHash = null, IsActive = true };
            users.GoogleUsers.Add(user); links.Add(identity.Subject, user); return Task.FromResult(user);
        }
    }
    public Task LinkGoogleAsync(Guid userId, VerifiedGoogleIdentity identity, CancellationToken ct)
    {
        lock (gate)
        {
            var user = new[] { users.User }.Concat(users.GoogleUsers).Single(u => u.Id == userId);
            if (!user.IsActive) throw new UnauthorizedAccessException();
            if (!string.Equals(user.Email, identity.Email, StringComparison.OrdinalIgnoreCase) ||
                (links.TryGetValue(identity.Subject, out var existing) && existing.Id != userId)) throw new ExternalLoginConflictException();
            links[identity.Subject] = user; return Task.CompletedTask;
        }
    }
}

public sealed class MemorySignup(MemoryUsers users,MemoryProfiles profiles):ISignupRepository
{
 public bool Fail;
 public async Task<User> CreateAsync(SignupAccount input,CancellationToken ct)
 {
  if(Fail)throw new InvalidOperationException("Private database failure: do not expose.");
  if(await FindByEmailAsync(input.Email,ct) is not null)throw new SignupException(409,"An account already exists. Sign in to continue.");
  var user=new User{Id=Guid.NewGuid(),UserName="user_"+Guid.NewGuid().ToString("N"),Email=input.Email,PasswordHash=input.PasswordHash,IsActive=true};users.GoogleUsers.Add(user);
  await profiles.UpdateAsync(user.Id,new(input.FirstName,input.LastName,input.Phone),DateTime.UtcNow,ct);return user;
 }
 public Task<User?> FindByEmailAsync(string email,CancellationToken ct)
 {var matches=users.GoogleUsers.Append(users.User).Where(u=>string.Equals(u.Email,email.Trim(),StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();return Task.FromResult(matches.Length==1?matches[0]:null);}
}

public sealed class FakeApplicationProviders : IProviderLoginAccessClient
{
    public Dictionary<Guid,ProviderLoginAccess> Items { get; } = new();
    public bool Unavailable { get; set; }
    public Task<ProviderLoginAccess?> GetAsync(Guid userId,CancellationToken ct)
    {
        if(Unavailable)throw new ApplicationLoginException(503,"Provider sign-in validation is temporarily unavailable.");
        return Task.FromResult(Items.GetValueOrDefault(userId));
    }
}
public sealed class FakeApplicationAdmins : IPlatformAdminRepository
{
    public HashSet<Guid> Users { get; } = new();
    public Task<bool> IsAdminAsync(Guid userId,CancellationToken ct)=>Task.FromResult(Users.Contains(userId));
}
