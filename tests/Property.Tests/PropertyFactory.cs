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
using Zumaris.Property.Application.Abstractions;
using Zumaris.Property.Application.Features;
using Zumaris.Property.Domain.Entities;

namespace Property.Tests;

public sealed class PropertyFactory : WebApplicationFactory<Program>
{
    public const string Key="test-only-property-signing-key-do-not-deploy-123456789";
    public Guid UserId { get; }=Guid.NewGuid();
    public MemoryProperties Repository { get; }=new();
    public IPropertyRepository? PersistenceOverride { get; init; }
    public FakeIdentity Validator { get; }=new();
    public string EnvironmentName { get; init; }="Development";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
        { ["Jwt:Key"]=Key,["Jwt:Issuer"]="property-tests",["Jwt:Audience"]="property-clients" }));
        builder.ConfigureServices(services=>
        {
            services.RemoveAll<IPropertyRepository>();services.RemoveAll<IIdentitySessionValidator>();
            services.AddSingleton<IPropertyRepository>(PersistenceOverride??Repository);services.AddSingleton<IIdentitySessionValidator>(Validator);
        });
    }
    public HttpClient Client(bool authenticated=true,Guid? userId=null,string? signingKey=null,bool sessionClaim=true)
    {
        var client=CreateClient(new() { AllowAutoRedirect=false });
        if(authenticated)
        {
            var claims=new List<Claim> { new("sub",(userId??UserId).ToString()) };
            if(sessionClaim)claims.Add(new("sid",Guid.NewGuid().ToString()));
            var token=new JwtSecurityToken("property-tests","property-clients",claims,DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey??Key)),SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }
    public static CreateProperty Valid()=>new("12 Example Road","NW11 8HB","London","GB",51.556m,-0.192m,
        PropertyType.Terraced,3,2,1985,OwnershipRole.Owner);
}
public sealed class FakeIdentity : IIdentitySessionValidator
{
    public bool Active { get; set; }=true;
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }
    public Task<bool> IsActiveAsync(string token,bool originalRequestIsHttps,CancellationToken ct)
    {
        Calls++;if(Unavailable)throw new HttpRequestException("Test Identity unavailable.");
        return Task.FromResult(Active);
    }
}
// HTTP contract fixture. Actual SQL/FK/locking behavior is validated separately on MySQL.
public sealed class MemoryProperties : IPropertyRepository
{
    private readonly object gate=new();
    public Dictionary<Guid,PropertyRecord> Records { get; }=[];
    public Dictionary<(Guid Property,Guid User),OwnershipRole> Members { get; }=[];
    public Dictionary<Guid,Guid?> Current { get; }=[];
    public List<(Guid Property,Guid User,string Kind,OwnershipRole? From,OwnershipRole? To)> History { get; }=[];
    private PropertyAccess? Find(Guid user,Guid id)=>Records.TryGetValue(id,out var p)&&p.IsActive&&Members.TryGetValue((id,user),out var role)
        ? new(p,role,Current.GetValueOrDefault(user)==id):null;
    private PropertyAccess Owned(Guid user,Guid id,int version)
    {
        var item=Find(user,id)??throw new PropertyNotFoundException();
        if(item.Property.CreatedByUserId!=user)throw new PropertyForbiddenException();
        if(item.Property.Version!=version)throw new PropertyConflictException();return item;
    }
    public Task<PropertyAccess> CreateAsync(PropertyRecord p,OwnershipRole role,CancellationToken ct)
    {
        lock(gate)
        {
            Records.Add(p.PropertyId,p);Members.Add((p.PropertyId,p.CreatedByUserId),role);
            if(!Current.TryGetValue(p.CreatedByUserId,out var id)||!id.HasValue||Find(p.CreatedByUserId,id.Value) is null)Current[p.CreatedByUserId]=p.PropertyId;
            History.Add((p.PropertyId,p.CreatedByUserId,"Created",null,role));return Task.FromResult(Find(p.CreatedByUserId,p.PropertyId)!);
        }
    }
    public Task<PropertyAccess?> GetAsync(Guid user,Guid id,CancellationToken ct)
    { lock(gate)return Task.FromResult(Find(user,id)); }
    public Task<PropertyAccess?> GetCurrentAsync(Guid user,CancellationToken ct)
    { lock(gate)return Task.FromResult(Current.GetValueOrDefault(user) is { } id?Find(user,id):null); }
    public Task<(IReadOnlyList<PropertyAccess> Items,long Total)> ListAsync(Guid user,int page,int size,CancellationToken ct)
    {
        lock(gate)
        {
            var all=Records.Keys.Select(id=>Find(user,id)).Where(x=>x is not null).Select(x=>x!)
                .OrderByDescending(x=>x.IsCurrent).ThenByDescending(x=>x.Property.CreatedAtUtc).ThenByDescending(x=>x.Property.PropertyId).ToArray();
            return Task.FromResult(((IReadOnlyList<PropertyAccess>)all.Skip((int)Math.Min(((long)page-1)*size,int.MaxValue)).Take(size).ToArray(),(long)all.Length));
        }
    }
    public Task<PropertyAccess> UpdateAsync(Guid user,Guid id,CreateProperty p,int version,DateTime now,CancellationToken ct)
    {
        lock(gate)
        {
            var a=Owned(user,id,version);var x=a.Property;
            Records[id]=x with { AddressLine=p.AddressLine,Postcode=p.Postcode,City=p.City,CountryCode=p.CountryCode,Latitude=p.Latitude,Longitude=p.Longitude,
                PropertyType=p.PropertyType,Bedrooms=p.Bedrooms,Bathrooms=p.Bathrooms,YearBuilt=p.YearBuilt,UpdatedAtUtc=now,Version=version+1 };
            Members[(id,user)]=p.OwnershipRole;
            if(a.OwnershipRole!=p.OwnershipRole)History.Add((id,user,"RoleChanged",a.OwnershipRole,p.OwnershipRole));
            return Task.FromResult(Find(user,id)!);
        }
    }
    public Task ArchiveAsync(Guid user,Guid id,int version,DateTime now,CancellationToken ct)
    {
        lock(gate)
        {
            var item=Owned(user,id,version);Records[id]=item.Property with { IsActive=false,Version=version+1,UpdatedAtUtc=now };
            foreach(var m in Members.Keys.Where(m=>m.Property==id))History.Add((id,m.User,"PropertyArchived",Members[m],null));
            foreach(var u in Current.Where(x=>x.Value==id).Select(x=>x.Key).ToArray())Current[u]=null;
            if(item.IsCurrent)Current[user]=Records.Values.Where(p=>p.IsActive&&Members.ContainsKey((p.PropertyId,user))).OrderByDescending(p=>p.CreatedAtUtc).ThenByDescending(p=>p.PropertyId).Select(p=>(Guid?)p.PropertyId).FirstOrDefault();
            return Task.CompletedTask;
        }
    }
    public Task<PropertyAccess> SetCurrentAsync(Guid user,Guid id,CancellationToken ct)
    { lock(gate){ var a=Find(user,id)??throw new PropertyNotFoundException();Current[user]=id;return Task.FromResult(a with { IsCurrent=true }); } }
}
