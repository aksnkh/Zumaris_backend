using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Zumaris.Provider.Application;
using Xunit;
namespace Provider.Tests;
public class ApplicationIsolationTests
{
    [Theory]
    [InlineData("App","GET","/api/providers/me")]
    [InlineData("App","GET","/api/providers/me/verification-documents")]
    [InlineData("App","POST","/api/providers/me/verification-documents/Insurance/files")]
    [InlineData("App","GET","/api/providers/me/documents/11111111-1111-4111-8111-111111111111")]
    [InlineData("App","PUT","/api/providers/me")]
    [InlineData("App","GET","/api/admin/providers")]
    [InlineData("Tech","GET","/api/admin/verification-documents")]
    [InlineData("Tech","POST","/api/admin/providers/11111111-1111-4111-8111-111111111111/verification-documents/Insurance/review")]
    [InlineData("Admin","GET","/api/providers/me/verification-documents")]
    [InlineData("Admin","POST","/api/providers/signup")]
    public async Task Application_boundaries_apply_to_direct_service_requests(string application,string method,string path)
    {
        using var f=new Factory();using var c=f.Client(application);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.SendAsync(new HttpRequestMessage(new(method),path))).StatusCode);
    }
    [Fact]
    public async Task Tech_can_read_its_documents_while_customer_can_only_begin_provider_signup()
    {
        using var f=new Factory();using var tech=f.Client("Tech");using var customer=f.Client("App");
        Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/verification-documents")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await customer.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=false})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await customer.GetAsync("/api/providers/me/verification-documents")).StatusCode);
    }
    [Theory]
    [InlineData("MissingApplication")][InlineData("WrongRole")][InlineData("DuplicateApplication")][InlineData("MissingProviderId")]
    public async Task Invalid_signed_application_contexts_are_denied(string mutation)
    {
        using var f=new Factory();using var c=f.CreateClient();var claims=f.Claims("Tech").ToList();
        if(mutation=="MissingApplication")claims.RemoveAll(x=>x.Type=="application");
        if(mutation=="WrongRole"){claims.RemoveAll(x=>x.Type=="role");claims.Add(new("role","Customer"));}
        if(mutation=="DuplicateApplication")claims.Add(new("application","App"));
        if(mutation=="MissingProviderId")claims.RemoveAll(x=>x.Type=="providerId");
        c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("clients",claims));
        Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/providers/me/verification-documents")).StatusCode);
    }
    [Fact]
    public async Task Provider_login_lookup_accepts_only_a_purpose_bound_internal_credential()
    {
        using var f=new Factory();var path=$"/api/internal/users/{f.User}/login-access";
        using var customer=f.Client("App");Assert.Equal(HttpStatusCode.Unauthorized,(await customer.GetAsync(path)).StatusCode);
        using var internalClient=f.CreateClient();internalClient.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Zumaris.Provider.LoginAccess",[new("sub",f.User.ToString()),new("scope","provider-login-access")],30));
        Assert.Equal(HttpStatusCode.OK,(await internalClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await internalClient.GetAsync($"/api/internal/users/{Guid.NewGuid()}/login-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await internalClient.GetAsync("/api/providers/me/verification-documents")).StatusCode);
        foreach(var pair in new[]{("provider-login-access",300),("anything-else",30)})
        {
            internalClient.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("Zumaris.Provider.LoginAccess",[new("sub",f.User.ToString()),new("scope",pair.Item1)],pair.Item2));
            Assert.Equal(HttpStatusCode.Unauthorized,(await internalClient.GetAsync(path)).StatusCode);
        }
    }
    public sealed class Factory:WebApplicationFactory<Program>
    {
        const string Key="application-isolation-test-only-key-123456789012345";
        public readonly Guid User=Guid.NewGuid();public readonly Guid Provider=Guid.NewGuid();
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));
            b.ConfigureServices(s=>
            {
                s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator,ActiveSession>();
                s.RemoveAll<IProviderVerificationRepository>();s.AddSingleton<IProviderVerificationRepository,Documents>();
                s.RemoveAll<IProviderLoginAccessRepository>();s.AddSingleton<IProviderLoginAccessRepository>(new LoginRepository(User,Provider));
            });
        }
        public IEnumerable<Claim> Claims(string app)=>[new("sub",User.ToString()),new("sid",Guid.NewGuid().ToString()),new("application",app),new("role",app=="Tech"?"Provider":app=="Admin"?"Admin":"Customer"),new("providerId",Provider.ToString())];
        public string Token(string audience,IEnumerable<Claim> claims,int seconds=300)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("tests",audience,claims,DateTime.UtcNow,DateTime.UtcNow.AddSeconds(seconds),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256)));
        public HttpClient Client(string app){var c=CreateClient();c.DefaultRequestHeaders.Authorization=new("Bearer",Token("clients",Claims(app)));return c;}
    }
    private sealed class ActiveSession:IIdentitySessionValidator
    {public Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class LoginRepository(Guid user,Guid provider):IProviderLoginAccessRepository
    {public Task<ProviderLoginAccess?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<ProviderLoginAccess?>(id==user?new(user,provider,true,true):null);}
    private sealed class Documents:IProviderVerificationRepository
    {
        public Task<VerificationOverview> OverviewAsync(Guid owner,CancellationToken ct)=>Task.FromResult(new VerificationOverview([],new(null,null)));
        public Task<VerificationProfile> SaveProfileAsync(Guid owner,SaveVerificationProfile input,CancellationToken ct)=>throw new NotSupportedException();
        public Task<VerificationDocumentDetails> DetailsAsync(Guid? owner,Guid? provider,VerificationDocumentType type,bool admin,CancellationToken ct)=>throw new NotSupportedException();
        public Task UploadAsync(Guid owner,VerificationDocumentType type,ProviderDocumentUpload input,byte[] hash,IdentityDocumentKind? kind,DateTime now,CancellationToken ct)=>throw new NotSupportedException();
        public Task SubmitAsync(Guid owner,IReadOnlyList<Guid> ids,DateTime now,CancellationToken ct)=>throw new NotSupportedException();
        public Task<VerificationOverview> AdminOverviewAsync(Guid provider,CancellationToken ct)=>throw new NotSupportedException();
        public Task ReviewAsync(Guid admin,Guid provider,VerificationDocumentType type,ReviewVerificationDocument input,DateTime now,CancellationToken ct)=>throw new NotSupportedException();
        public Task<PendingVerificationPage> PendingAsync(int page,int size,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentReviewAudit>> AuditAsync(Guid provider,CancellationToken ct)=>throw new NotSupportedException();
    }
}
