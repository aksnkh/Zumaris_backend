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
    public HistoryRepositoryFake HistoryRepository { get; } = new();
    public SessionValidator Validator { get; } = new();
    public TestClock Clock { get; } = new();
    public string UploadRoot { get; } = Path.Combine(Path.GetTempPath(), "zumaris-test-photos-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "request-tests", ["Jwt:Audience"] = "request-clients", ["Storage:RootPath"] = UploadRoot }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceRequestRepository>(); services.RemoveAll<IIdentitySessionValidator>();
            services.RemoveAll<IServiceHistoryRepository>();
            services.AddSingleton<IServiceHistoryRepository>(HistoryRepository);
            services.RemoveAll<TimeProvider>(); services.RemoveAll<IRequestLifecycleRepository>();
            services.AddSingleton<IServiceRequestRepository>(Repository);
            services.AddSingleton<IRequestLifecycleRepository>(Repository);
            services.AddSingleton<IIdentitySessionValidator>(Validator);
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Client(bool authenticated = true, string? signingKey = null, Guid? userId = null)
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        if (authenticated)
        {
            var token = new JwtSecurityToken("request-tests", "request-clients",
                [new Claim("sub", (userId ?? UserId).ToString()), new Claim("sid", Guid.NewGuid().ToString())],
                DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(UploadRoot)) Directory.Delete(UploadRoot, true);
    }

    public static CreateServiceRequest Valid() => new(Guid.NewGuid(), 1, 1,
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
public sealed class MemoryRequests : IServiceRequestRepository, IRequestLifecycleRepository
{
    private readonly object gate = new();
    public List<PhotoRecord> Photos { get; } = [];
    public List<(Guid RequestId, RequestStatusHistoryDto Event)> History { get; } = [];
    public bool ConflictOnAttach { get; set; }
    public bool FailOnAttach { get; set; }

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
            History.Add((request.RequestId, new RequestStatusHistoryDto(Guid.NewGuid(), null, RequestStatus.Submitted, request.UserId, request.CreatedAtUtc, null)));
            if (key is not null) keys.Add((request.UserId, key), persisted);
            return Task.FromResult(new CreateResult(ServiceRequestService.ToCreated(request), false));
        }
    }

    private ServiceRequestRecord Owned(Guid user, Guid id) => Records.SingleOrDefault(r => r.Request.RequestId == id && r.Request.UserId == user)?.Request ?? throw new RequestNotFoundException();
    private ServiceRequestRecord Locked(Guid user, Guid id, int version)
    {
        var current = Owned(user, id);
        if (current.Version != version) throw new RequestConflictException("Stale version.");
        return current;
    }
    private void Replace(ServiceRequestRecord record)
    {
        var index = Records.FindIndex(r => r.Request.RequestId == record.RequestId);
        Records[index] = Records[index] with { Request = record };
        foreach (var key in keys.Where(k => k.Value.Request.RequestId == record.RequestId).Select(k => k.Key).ToList())
            keys[key] = keys[key] with { Request = record };
    }
    public Task<ServiceRequestRecord?> GetAsync(Guid user, Guid id, CancellationToken ct)
    { lock (gate) return Task.FromResult(Records.SingleOrDefault(r => r.Request.RequestId == id && r.Request.UserId == user)?.Request); }
    public Task<(IReadOnlyList<ServiceRequestRecord> Items, long Total)> ListAsync(Guid user, RequestStatus? status, int page, int size, CancellationToken ct)
    {
        lock (gate)
        {
            var items = Records.Select(r => r.Request).Where(r => r.UserId == user && (!status.HasValue || r.Status == status)).OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.RequestId).ToList();
            return Task.FromResult(((IReadOnlyList<ServiceRequestRecord>)items.Skip((page - 1) * size).Take(size).ToList(), (long)items.Count));
        }
    }
    public Task<IReadOnlyList<PhotoRecord>> PhotosAsync(Guid user, IReadOnlyList<Guid> ids, CancellationToken ct)
    { lock (gate) return Task.FromResult<IReadOnlyList<PhotoRecord>>(Photos.Where(p => ids.Contains(p.RequestId) && Records.Any(r => r.Request.RequestId == p.RequestId && r.Request.UserId == user)).ToList()); }
    public Task<ServiceRequestRecord> UpdateAsync(Guid user, Guid id, CreateServiceRequest request, int version, DateTime now, CancellationToken ct)
    {
        lock (gate)
        {
            var r = Locked(user, id, version);
            if (r.Status != RequestStatus.Submitted) throw new RequestConflictException("Not editable.");
            r = r with { PropertyId = request.PropertyId, CategoryId = request.CategoryId, AreaTypeId = request.AreaTypeId,
                Description = request.Description, Urgency = request.Urgency, PreferredDate = request.PreferredDate,
                PreferredStartTime = request.PreferredStartTime, PreferredEndTime = request.PreferredEndTime, Version = version + 1, UpdatedAtUtc = now };
            Replace(r); return Task.FromResult(r);
        }
    }
    public Task<ServiceRequestRecord> CancelAsync(Guid user, Guid id, string reason, int version, DateTime now, CancellationToken ct)
    {
        lock (gate)
        {
            var r = Locked(user, id, version);
            if (r.Status is not (RequestStatus.Submitted or RequestStatus.Assigned)) throw new RequestConflictException("Not cancellable.");
            History.Add((id, new(Guid.NewGuid(), r.Status, RequestStatus.Cancelled, user, now, reason)));
            r = r with { Status = RequestStatus.Cancelled, Version = version + 1, UpdatedAtUtc = now };
            Replace(r); return Task.FromResult(r);
        }
    }
    public Task<IReadOnlyList<RequestStatusHistoryDto>> TimelineAsync(Guid user, Guid id, CancellationToken ct)
    { lock (gate) { Owned(user, id); return Task.FromResult<IReadOnlyList<RequestStatusHistoryDto>>(History.Where(h => h.RequestId == id).Select(h => h.Event).ToList()); } }
    public Task<int> AttachPhotosAsync(Guid user, Guid id, int version, IReadOnlyList<StoredPhoto> photos, DateTime now, CancellationToken ct)
    {
        lock (gate)
        {
            if (FailOnAttach) throw new IOException("Simulated ambiguous database commit error.");
            if (ConflictOnAttach) throw new RequestConflictException("Simulated concurrent mutation.");
            var r = Locked(user, id, version);
            if (r.Status is not (RequestStatus.Submitted or RequestStatus.Assigned)) throw new RequestConflictException("Not attachable.");
            var slots = Photos.Where(p => p.RequestId == id).Select(p => p.Slot).ToHashSet();
            if (slots.Count + photos.Count > 8) throw new RequestConflictException("At most 8 photos.");
            foreach (var p in photos)
            {
                var slot = Enumerable.Range(0, 8).First(s => !slots.Contains(s)); slots.Add(slot);
                Photos.Add(new(p.Id, id, p.FilePath, slot, now));
            }
            Replace(r with { Version = version + 1, UpdatedAtUtc = now }); return Task.FromResult(version + 1);
        }
    }
}
