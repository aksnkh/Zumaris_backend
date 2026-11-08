using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Xunit;
namespace Provider.Tests;
[Collection("Provider MySQL")]
public class OnboardingWorkflowTests
{
 [Fact]
 public async Task Swagger_documents_onboarding_admin_and_review_routes_without_database_access()
 {
  using var f=new Factory("Server=127.0.0.1;Database=unused;User=test");using var c=f.Client(f.User);
  var json=await c.GetStringAsync("/swagger/v1/swagger.json");
  Assert.Contains("/api/providers/me/documents",json);Assert.Contains("/api/admin/providers/{id}/verification",json);Assert.Contains("/api/service-requests/{requestId}/review",json);
 }

 [ProviderMySqlFact]
 public async Task Documents_admin_review_self_declared_specialties_and_customer_reviews_are_authorized()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);
  Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_provider_test",parsed.Database);
  await using var db=new MySqlConnection(cs);await db.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  foreach(var file in new[]{"001_provider_quote_profiles.sql","002_onboarding_reviews.sql","004_independent_document_reviews.sql"})
  {await using var cmd=new MySqlCommand((await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider",file))).Replace("zumaris_provider","zumaris_provider_test"),db);await cmd.ExecuteNonQueryAsync();}
  using var f=new Factory(cs);using var tech=f.Client(f.User);using var admin=f.Client(f.Admin);using var stranger=f.Client(Guid.NewGuid());
  Guid id=Guid.Empty;
  try
  {
   Assert.Equal(HttpStatusCode.NotFound,(await tech.GetAsync("/api/providers/me")).StatusCode);
   Assert.Equal(HttpStatusCode.BadRequest,(await tech.PostAsJsonAsync("/api/providers/me",new{displayName="Tech",categoryIds=new[]{1},isVerified=true})).StatusCode);
   var saved=await tech.PostAsJsonAsync("/api/providers/me",new{displayName="Verified technician",categoryIds=new[]{1}});Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
   var account=(await saved.Content.ReadFromJsonAsync<ProviderAccount>())!;id=account.ProviderId;Assert.Equal("Draft",account.OnboardingStatus);
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/admin/providers")).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{id}/verification",new{revision=account.Revision,approved=true})).StatusCode);
   using var upload=new MultipartFormDataContent();upload.Add(new StringContent("Identity"),"kind");upload.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nTest document")),"file","identity.pdf");
   var uploaded=await tech.PostAsync("/api/providers/me/documents",upload);Assert.Equal(HttpStatusCode.OK,uploaded.StatusCode);var doc=(await uploaded.Content.ReadFromJsonAsync<ProviderDocument>())!;
   account=(await tech.GetFromJsonAsync<ProviderAccount>("/api/providers/me"))!;Assert.Equal("Draft",account.OnboardingStatus);
   Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/providers/me/documents/{doc.Id}")).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/admin/providers/{id}/documents/{doc.Id}")).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{id}/verification",new{revision=account.Revision-1,approved=true})).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{id}/verification",new{revision=account.Revision,approved=true})).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await tech.PutAsJsonAsync("/api/providers/me/verification-documents/profile",new{postcode="SW1A 1AA"})).StatusCode);
   foreach(var kind in new[]{"IdentityCheck","Insurance","Certificate"})
   {
    using var file=new MultipartFormDataContent();if(kind=="IdentityCheck")file.Add(new StringContent("Passport"),"identityKind");file.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n"+kind)),"file",kind+".pdf");
    var uploadedTyped=await tech.PostAsync($"/api/providers/me/verification-documents/{kind}/files",file);Assert.Equal(HttpStatusCode.OK,uploadedTyped.StatusCode);
    using var typedJson=System.Text.Json.JsonDocument.Parse(await uploadedTyped.Content.ReadAsStringAsync());var fileId=typedJson.RootElement.GetProperty("payload").GetProperty("document").GetProperty("currentDocument").GetProperty("documentId").GetGuid();
    Assert.Equal(HttpStatusCode.OK,(await tech.PostAsJsonAsync($"/api/providers/me/verification-documents/{kind}/submit",new{documentId=fileId})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync($"/api/admin/providers/{id}/verification-documents/{kind}/review",new{documentId=fileId,approved=true})).StatusCode);
   }
   var eligible=await tech.GetFromJsonAsync<ProviderEligibility>("/api/providers/me/eligibility");Assert.Contains(1,eligible!.CategoryIds);
   Assert.Equal(HttpStatusCode.OK,(await tech.PutAsJsonAsync("/api/providers/me",new{displayName="Verified technician",categoryIds=new[]{1,2}})).StatusCode);
   eligible=await tech.GetFromJsonAsync<ProviderEligibility>("/api/providers/me/eligibility");Assert.Contains(2,eligible!.CategoryIds);
   Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/quote-profile?categoryId=2")).StatusCode);
   using var customer=f.Client(f.Customer);f.Work.Work=new(Guid.NewGuid(),f.Customer,id,"Completed",Guid.NewGuid(),95,"GBP");var review=$"/api/service-requests/{f.Work.Work.RequestId}/review";
   Assert.Equal(HttpStatusCode.Forbidden,(await stranger.PostAsJsonAsync(review,new{rating=5,comment="Good work"})).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await customer.PostAsJsonAsync(review,new{rating=5,comment="Good work"})).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await customer.PostAsJsonAsync(review,new{rating=5,comment="Good work"})).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await customer.PostAsJsonAsync(review,new{rating=1,comment="Changed"})).StatusCode);
   var profile=await tech.GetFromJsonAsync<ProviderQuoteProfile>("/api/providers/me/quote-profile?categoryId=1");Assert.Equal(5m,profile!.Rating);Assert.Equal(3,profile.CompletedJobs);
   Assert.Equal(HttpStatusCode.OK,(await tech.PutAsJsonAsync("/api/providers/me",new{displayName="Changed business identity",categoryIds=new[]{1,2}})).StatusCode);
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   Assert.Equal(HttpStatusCode.NotFound,(await tech.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);
  }
  finally
  {
   if(id!=Guid.Empty){await using var cmd=new MySqlCommand("DELETE FROM provider_document_review_history WHERE provider_id=@id;DELETE FROM provider_verification_profile WHERE provider_id=@id;DELETE FROM provider_verification_slots WHERE provider_id=@id;DELETE FROM provider_document_reviews WHERE provider_id=@id;DELETE FROM provider_reviews WHERE provider_id=@id;DELETE FROM provider_verification_history WHERE provider_id=@id;DELETE FROM provider_documents WHERE provider_id=@id;DELETE FROM provider_onboarding WHERE provider_id=@id;DELETE FROM provider_category_qualifications WHERE provider_id=@id;DELETE FROM providers WHERE id=@id;",db);cmd.Parameters.AddWithValue("@id",id.ToString());await cmd.ExecuteNonQueryAsync();}
  }
 }
 private class Factory(string cs):WebApplicationFactory<Program>
 {
  const string Key="test-only-onboarding-key-12345678901234567890";public Guid User=Guid.NewGuid(),Admin=Guid.NewGuid(),Customer=Guid.NewGuid();public WorkFake Work=new();
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {
   b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"},{"ConnectionStrings:DefaultConnection",cs}}));
   b.ConfigureServices(s=>{s.RemoveAll<IProviderWorkflowClient>();Work.Admin=Admin;s.AddSingleton<IProviderWorkflowClient>(Work);s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator,Session>();});
  }
  public HttpClient Client(Guid user){var c=CreateClient();var t=new JwtSecurityToken("tests","clients",new[]{new Claim("sub",user.ToString()),new Claim("sid",Guid.NewGuid().ToString()),new Claim("application",(user==Admin?"Admin":user==Customer?"App":"Tech")),new Claim("role",((user==Admin?"Admin":user==Customer?"App":"Tech")=="Tech"?"Provider":(user==Admin?"Admin":user==Customer?"App":"Tech")=="Admin"?"Admin":"Customer")),new Claim("providerId",Guid.NewGuid().ToString())},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(t));return c;}
 }
 private class Session:IIdentitySessionValidator{public Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct)=>Task.FromResult(true);}
 private class WorkFake:IProviderWorkflowClient
 {
  public Guid Admin;public CompletedWork Work=null!;
  public Task<bool> IsAdminAsync(string token,CancellationToken ct)=>Task.FromResult(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject==Admin.ToString());
  public Task<CompletedWork> CompletedWorkAsync(Guid id,string token,CancellationToken ct)=>Task.FromResult(Work);
  public Task<int?> CompletedJobsAsync(Guid id,string token,CancellationToken ct)=>Task.FromResult<int?>(3);
 }
}
