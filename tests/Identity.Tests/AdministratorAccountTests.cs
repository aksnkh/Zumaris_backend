using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Signup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Identity.Tests;
public class AdministratorAccountTests
{
    private const string Key="test-only-administrator-bootstrap-key-12345678901234567890";
    private static object Input(string email="new-admin@example.test")=>new{firstName=" New ",lastName="Admin",email,phone="+44 7700 900123",password="Password-for-new-admin-1"};
    private static async Task<JsonElement> Read(HttpResponseMessage response,HttpStatusCode expected)
    {
        var body=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==expected,$"Expected {expected}, got {response.StatusCode}: {body}");
        using var json=JsonDocument.Parse(body);Assert.Equal((int)expected,json.RootElement.GetProperty("statusCode").GetInt32());Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));return json.RootElement.Clone();
    }
    [Fact]
    public async Task Bootstrap_is_disabled_by_default_without_accessing_storage()
    {
        using var f=new Fixture(false);using var c=f.Client();await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.NotFound);Assert.Equal(0,f.Repository.Calls);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("wrong-key")]
    public async Task Enabled_bootstrap_requires_the_server_secret(string? key)
    {
        using var f=new Fixture();using var c=f.Client();if(key is not null)c.DefaultRequestHeaders.Add("X-Zumaris-Bootstrap-Key",key);
        await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.Unauthorized);Assert.Equal(0,f.Repository.Calls);
    }
    [Fact]
    public async Task Missing_bootstrap_configuration_fails_closed()
    {
        using var f=new Fixture(key:"");using var c=f.Client();await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.ServiceUnavailable);Assert.Equal(0,f.Repository.Calls);
    }
    [Fact]
    public async Task Bootstrap_creates_one_account_without_a_session_then_normal_admin_login_works()
    {
        using var f=new Fixture();using var c=f.Client(key:true);
        var response=await c.PostAsJsonAsync("/api/admin/bootstrap",Input());var json=await Read(response,HttpStatusCode.Created);
        Assert.False(response.Headers.Contains("Set-Cookie"));Assert.Empty(f.Source.Sessions.Items);Assert.Equal("Admin",json.GetProperty("messageLevel").GetString());
        var payload=json.GetProperty("payload");var id=payload.GetProperty("userId").GetGuid();Assert.Equal("Admin",payload.GetProperty("role").GetString());Assert.Equal(JsonValueKind.Null,payload.GetProperty("createdByUserId").ValueKind);Assert.False(payload.TryGetProperty("password",out _));Assert.False(payload.TryGetProperty("accessToken",out _));
        Assert.Contains(id,f.Source.ApplicationAdmins.Users);Assert.NotEqual("Password-for-new-admin-1",Assert.Single(f.Source.Users.GoogleUsers).PasswordHash);
        var login=await c.PostAsJsonAsync("/api/auth/login",new{userName="new-admin@example.test",password="Password-for-new-admin-1",application="Admin"});
        var loginBody=await Read(login,HttpStatusCode.OK);Assert.Equal("Admin",loginBody.GetProperty("payload").GetProperty("role").GetString());
        await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input("second@example.test")),HttpStatusCode.Conflict);Assert.Single(f.Source.Users.GoogleUsers);
    }
    [Theory][InlineData("App")][InlineData("Tech")]
    public async Task Non_admin_tokens_cannot_create_an_administrator(string application)
    {
        using var f=new Fixture();using var c=f.Client();
        if(application=="Tech"){var tech=f.Source.Accounts.Seed("Tech");f.Source.ApplicationProviders.Items[tech.Id]=new(tech.Id,Guid.NewGuid(),true,true);}
        await f.Login(c,application);await Read(await c.PostAsJsonAsync("/api/admin/accounts",Input()),HttpStatusCode.Forbidden);Assert.Equal(0,f.Repository.Calls);
    }
    [Fact]
    public async Task Creation_requires_authentication_and_current_administrator_membership()
    {
        using var f=new Fixture();using var c=f.Client();await Read(await c.PostAsJsonAsync("/api/admin/accounts",Input()),HttpStatusCode.Unauthorized);
        f.Source.ApplicationAdmins.Users.Add(f.Source.Accounts.Seed("Admin").Id);await f.Login(c,"Admin");f.Source.ApplicationAdmins.Users.Clear();
        await Read(await c.PostAsJsonAsync("/api/admin/accounts",Input()),HttpStatusCode.Forbidden);Assert.Equal(0,f.Repository.Calls);
    }
    [Fact]
    public async Task Authorized_creation_does_not_replace_the_creators_session_or_promote_existing_email()
    {
        using var f=new Fixture();using var c=f.Client();var actor=f.Source.Accounts.Seed("Admin").Id;f.Source.ApplicationAdmins.Users.Add(actor);await f.Login(c,"Admin");
        var session=Assert.Single(f.Source.Sessions.Items).Key;
        var response=await c.PostAsJsonAsync("/api/admin/accounts",Input());var payload=(await Read(response,HttpStatusCode.Created)).GetProperty("payload");
        Assert.Equal(actor,payload.GetProperty("createdByUserId").GetGuid());Assert.False(response.Headers.Contains("Set-Cookie"));Assert.Equal(session,Assert.Single(f.Source.Sessions.Items).Key);
        await Read(await c.PostAsJsonAsync("/api/admin/accounts",Input()),HttpStatusCode.Conflict);
        Assert.Equal(2,f.Source.Users.GoogleUsers.Count);Assert.Equal(2,f.Source.ApplicationAdmins.Users.Count);
    }
    [Fact]
    public async Task Csrf_unknown_fields_and_invalid_account_data_are_rejected_before_creation()
    {
        using var f=new Fixture();using var noCsrf=f.Client(csrf:false,key:true);await Read(await noCsrf.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.BadRequest);
        using var c=f.Client(key:true);
        var unknown=new{firstName="New",lastName="Admin",email="a@example.test",phone="+447700900123",password="Password-for-new-admin-1",isAdmin=true};
        await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",unknown),HttpStatusCode.BadRequest);
        var invalid=await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",new{firstName="",lastName="Admin",email="bad",phone="07700900123",password="short"}),HttpStatusCode.BadRequest);Assert.True(invalid.TryGetProperty("errors",out _));Assert.Equal(0,f.Repository.Calls);
    }
    [Fact]
    public async Task Database_failures_are_not_exposed_and_bootstrap_cannot_bypass_existing_admins()
    {
        using var f=new Fixture();using var c=f.Client(key:true);f.Repository.Fail=true;
        var body=await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.InternalServerError);Assert.DoesNotContain("Private database",body.ToString());
        f.Repository.Fail=false;f.Source.ApplicationAdmins.Users.Add(f.Source.Accounts.Seed("Admin").Id);
        await Read(await c.PostAsJsonAsync("/api/admin/bootstrap",Input()),HttpStatusCode.Conflict);Assert.Single(f.Source.Users.GoogleUsers);
    }
    [Fact]
    public async Task Swagger_documents_both_new_endpoints_and_the_shared_request_contract()
    {
        using var f=new Fixture();using var c=f.Client();using var json=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));var paths=json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/admin/accounts",out _));Assert.True(paths.TryGetProperty("/api/admin/bootstrap",out _));
        var response=paths.GetProperty("/api/admin/accounts").GetProperty("post").GetProperty("responses");Assert.True(response.TryGetProperty("201",out _));
        Assert.Contains(paths.GetProperty("/api/admin/bootstrap").GetProperty("post").GetProperty("parameters").EnumerateArray(),x=>x.GetProperty("name").GetString()=="X-Zumaris-Bootstrap-Key"&&x.GetProperty("required").GetBoolean());
    }
    private sealed class Fixture:IDisposable
    {
        public AuthFactory Source{get;}=new();public WebApplicationFactory<Program> Host{get;}public FakeRepository Repository{get;}
        public Fixture(bool enabled=true,string key=Key)
        {
            Repository=new(Source);
            Host=Source.WithWebHostBuilder(b=>
            {
                b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"AdminBootstrap:Enabled",enabled?"true":"false"},{"AdminBootstrap:Key",key}}));
                b.ConfigureServices(s=>{s.RemoveAll<IAdministratorAccountRepository>();s.AddSingleton<IAdministratorAccountRepository>(Repository);});
            });
        }
        public HttpClient Client(bool csrf=true,bool key=false)
        {
            var c=Host.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false,HandleCookies=false});if(csrf)c.DefaultRequestHeaders.Add("X-Zumaris-CSRF","1");if(key)c.DefaultRequestHeaders.Add("X-Zumaris-Bootstrap-Key",Key);return c;
        }
        public async Task Login(HttpClient c,string application)
        {
            var json=await Read(await c.PostAsJsonAsync("/api/auth/login",new{userName="alice",password="Password-for-tests-1",application}),HttpStatusCode.OK);
            c.DefaultRequestHeaders.Authorization=new("Bearer",json.GetProperty("payload").GetProperty("accessToken").GetString());
        }
        public void Dispose(){Host.Dispose();Source.Dispose();}
    }
    private sealed class FakeRepository(AuthFactory source):IAdministratorAccountRepository
    {
        public int Calls;public bool Fail,Consumed;private readonly SemaphoreSlim gate=new(1,1);
        public async Task<AdministratorAccountResponse> CreateAsync(SignupAccount input,Guid? actor,DateTime now,CancellationToken ct)
        {
            Calls++;await gate.WaitAsync(ct);try
            {
                if(Fail)throw new InvalidOperationException("Private database error: do not disclose.");
                if(actor is null&&(Consumed||source.ApplicationAdmins.Users.Count>0))throw new SignupException(409,"Administrator bootstrap has already been completed.");
                if(actor.HasValue&&!source.ApplicationAdmins.Users.Contains(actor.Value))throw new SignupException(403,"Active administrator access is required.");
                var user=await source.Accounts.CreateAsync("Admin",input,ct);source.ApplicationAdmins.Users.Add(user.Id);Consumed=true;
                return new(user.Id,user.UserName,user.Email,input.FirstName,input.LastName,input.Phone,"Admin",now,actor);
            }
            finally{gate.Release();}
        }
    }
}
