using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using Zumaris.Provider.Application;
using Zumaris.Provider.Infrastructure.Persistence;
using Xunit;
namespace Provider.Tests;
public class QuoteEligibilityTests
{
    private class WorkflowFake:IProviderWorkflowClient
    {
        public Task<bool> IsAdminAsync(string b,CancellationToken ct)=>Task.FromResult(false);
        public Task<CompletedWork> CompletedWorkAsync(Guid id,string b,CancellationToken ct)=>throw new NotImplementedException();
        public Task<int?> CompletedJobsAsync(Guid id,string b,CancellationToken ct)=>Task.FromResult<int?>(null);
    }
    [Fact]
    public async Task Provider_eligibility_requires_real_authentication_qualification_and_active_session()
    {
        using var f=new Factory();using var anonymous=f.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);
        using var client=f.Client();
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/providers/me/quote-profile?categoryId=2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/providers/me/quote-profile?categoryId=0")).StatusCode);
        f.Session.Active=false;Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);
    }
    private class Factory:WebApplicationFactory<Program>
    {
        private const string Key="test-only-provider-quote-signing-key-123456789";public Session Session{get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));
            builder.ConfigureServices(services=>{
                services.RemoveAll<IProviderWorkflowClient>();services.AddSingleton<IProviderWorkflowClient,WorkflowFake>();
                services.RemoveAll<IProviderQuoteRepository>();services.AddSingleton<IProviderQuoteRepository,FakeRepository>();
                services.RemoveAll<IIdentitySessionValidator>();services.AddSingleton<IIdentitySessionValidator>(Session);
            });
        }
        public HttpClient Client(bool authenticated=true)
        {
            var c=CreateClient();if(authenticated){var token=new JwtSecurityToken("tests","clients",new[]{new Claim("sub",Guid.NewGuid().ToString()),new Claim("sid",Guid.NewGuid().ToString())},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new JwtSecurityTokenHandler().WriteToken(token));}return c;
        }
    }
    private class Session:IIdentitySessionValidator {public bool Active{get;set;}=true;public Task<bool>IsActiveAsync(string token,bool https,CancellationToken ct)=>Task.FromResult(Active);}
    private class FakeRepository:IProviderQuoteRepository
    {
        public Task<ProviderPublicSummary?> SummaryAsync(Guid id,CancellationToken ct)=>Task.FromResult<ProviderPublicSummary?>(new(id,"Verified provider"));
        public Task<ProviderQuoteProfile?>GetAsync(Guid? user,Guid? provider,int category,CancellationToken ct)=>Task.FromResult(category==1?new ProviderQuoteProfile(Guid.NewGuid(),user??Guid.NewGuid(),"Verified provider",null,null,null,true,category):null);
    }
    [Fact]
    public async Task Test_token_one_is_not_a_provider_identity()
    {
        using var f=new Factory();using var c=f.Client(false);c.DefaultRequestHeaders.Authorization=new("Bearer","1");
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);
    }
}
public sealed class ProviderMySqlFact:FactAttribute
{
    public ProviderMySqlFact(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")))Skip="Requires isolated loopback zumaris_provider_test database.";}
}
[Collection("Provider MySQL")]
public class ProviderMySqlTests
{
    [ProviderMySqlFact]
    public async Task Unverified_inactive_and_unqualified_providers_are_denied_by_real_database()
    {
        var cs=Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);
        Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_provider_test",parsed.Database);
        await using var c=new MySqlConnection(cs);await c.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
        var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider/001_provider_quote_profiles.sql"))).Replace("zumaris_provider","zumaris_provider_test");
        await using(var cmd=new MySqlCommand(sql,c))await cmd.ExecuteNonQueryAsync();
        var id=Guid.NewGuid();var user=Guid.NewGuid();
        await using(var cmd=new MySqlCommand("INSERT INTO providers(id,user_id,display_name)VALUES(@id,@user,'Test provider');INSERT INTO provider_category_qualifications(provider_id,category_id,is_qualified)VALUES(@id,1,1);",c))
        {cmd.Parameters.AddWithValue("@id",id.ToString());cmd.Parameters.AddWithValue("@user",user.ToString());await cmd.ExecuteNonQueryAsync();}
        var more=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider/002_onboarding_reviews.sql"))).Replace("zumaris_provider","zumaris_provider_test");
        await using(var cmd=new MySqlCommand(more,c))await cmd.ExecuteNonQueryAsync();
        var repo=new ProviderQuoteRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build());
        try
        {
            Assert.Null(await repo.GetAsync(user,null,1,default));
            await using(var cmd=new MySqlCommand("UPDATE providers SET is_verified=1 WHERE id=@id",c)){cmd.Parameters.AddWithValue("@id",id.ToString());await cmd.ExecuteNonQueryAsync();}
            Assert.Null(await repo.GetAsync(user,null,1,default));
            await using(var ready=new MySqlCommand("INSERT INTO provider_onboarding(provider_id,reviewed_revision)VALUES(@id,1);INSERT INTO provider_documents(id,provider_id,kind,file_name,content_type,content,created_at)VALUES(UUID(),@id,'Identity','id.pdf','application/pdf',X'255044462D',UTC_TIMESTAMP(6));",c)){ready.Parameters.AddWithValue("@id",id.ToString());await ready.ExecuteNonQueryAsync();}
            var profile=await repo.GetAsync(user,null,1,default);Assert.NotNull(profile);Assert.Equal(id,profile.ProviderId);Assert.Null(profile.Rating);
            Assert.Null(await repo.GetAsync(user,null,2,default));
            Assert.NotNull(await repo.GetAsync(null,id,1,default));
            await using(var cmd=new MySqlCommand("UPDATE providers SET is_active=0 WHERE id=@id",c)){cmd.Parameters.AddWithValue("@id",id.ToString());await cmd.ExecuteNonQueryAsync();}
            Assert.Null(await repo.GetAsync(user,null,1,default));
            Assert.Equal("Test provider",(await repo.SummaryAsync(id,default))!.DisplayName);
        }
        finally
        {await using var cmd=new MySqlCommand("DELETE FROM provider_documents WHERE provider_id=@id;DELETE FROM provider_onboarding WHERE provider_id=@id;DELETE FROM provider_category_qualifications WHERE provider_id=@id;DELETE FROM providers WHERE id=@id",c);cmd.Parameters.AddWithValue("@id",id.ToString());await cmd.ExecuteNonQueryAsync();}
    }
}
