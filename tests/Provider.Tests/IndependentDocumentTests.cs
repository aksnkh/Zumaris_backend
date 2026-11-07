using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using Zumaris.Provider.Application;
using Xunit;
namespace Provider.Tests;
[Collection("Provider MySQL")]
public class IndependentDocumentTests
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter()}};
 static async Task<T> Payload<T>(HttpResponseMessage response,HttpStatusCode expected=HttpStatusCode.OK)
 {
  var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==expected,$"Expected {expected}, got {response.StatusCode}: {text}");using var json=JsonDocument.Parse(text);
  Assert.Equal((int)expected,json.RootElement.GetProperty("statusCode").GetInt32());Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));return json.RootElement.GetProperty("payload").Deserialize<T>(Json)!;
 }
 [Theory]
 [InlineData("GET","/api/providers/me/verification-documents")]
 [InlineData("GET","/api/providers/me/verification-documents/Insurance")]
 [InlineData("POST","/api/providers/me/verification-documents/Insurance/files")]
 [InlineData("POST","/api/providers/me/verification-documents/submit")]
 [InlineData("GET","/api/admin/verification-documents")]
 public async Task Document_endpoints_require_a_real_session_and_use_Response_errors(string method,string path)
 {
  using var f=new Factory();using var c=f.Client(false);using var response=await c.SendAsync(new HttpRequestMessage(new HttpMethod(method),path));Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(401,json.RootElement.GetProperty("statusCode").GetInt32());Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("payload").ValueKind);
 }
 [Fact]
 public async Task Role_checks_input_validation_and_swagger_do_not_require_database_access()
 {
  using var f=new Factory();using var c=f.Client();using var admin=f.Client(user:f.Admin);
  Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/admin/verification-documents")).StatusCode);
  var body=await c.PostAsJsonAsync("/api/providers/me/verification-documents/submit",new{documentIds=Array.Empty<Guid>()});Assert.Equal(HttpStatusCode.BadRequest,body.StatusCode);using var json=JsonDocument.Parse(await body.Content.ReadAsStringAsync());Assert.True(json.RootElement.TryGetProperty("errors",out _));Assert.Equal(400,json.RootElement.GetProperty("statusCode").GetInt32());
  Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/api/providers/me/verification-documents/999")).StatusCode);
  using(var bad=new MultipartFormDataContent()){bad.Add(new ByteArrayContent(new byte[]{1,2,3}),"file","bad.pdf");Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/api/providers/me/verification-documents/Insurance/files",bad)).StatusCode);}
  Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync($"/api/admin/providers/{Guid.NewGuid()}/verification-documents/Insurance/review",new{documentId=Guid.NewGuid(),approved=false})).StatusCode);
  using var swagger=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));var paths=swagger.RootElement.GetProperty("paths");Assert.True(paths.TryGetProperty("/api/providers/me/verification-documents",out _));Assert.True(paths.TryGetProperty("/api/admin/providers/{providerId}/verification-documents/{documentType}/review",out _));
  using(var missingIdentity=new MultipartFormDataContent()){missingIdentity.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7")),"file","id.pdf");Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/api/providers/me/verification-documents/IdentityCheck/files",missingIdentity)).StatusCode);}
  using(var invalidIdentity=new MultipartFormDataContent()){invalidIdentity.Add(new StringContent("Passport"),"identityKind");invalidIdentity.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7")),"file","insurance.pdf");Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/api/providers/me/verification-documents/Insurance/files",invalidIdentity)).StatusCode);}
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync("/api/providers/me/verification-documents/profile",new{postcode=""})).StatusCode);
  f.Session.Active=false;Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/providers/me/verification-documents")).StatusCode);
 }
 [ProviderMySqlFact]
 public async Task Independent_reviews_preserve_previous_approval_and_gate_new_work_on_all_required_documents_and_postcode()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_provider_test",parsed.Database);
  await using var db=new MySqlConnection(cs);await db.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  foreach(var file in new[]{"001_provider_quote_profiles.sql","002_onboarding_reviews.sql","003_signup.sql","004_independent_document_reviews.sql"})
  for(int i=0;i<2;i++){await using var cmd=new MySqlCommand((await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider",file))).Replace("zumaris_provider","zumaris_provider_test"),db);await cmd.ExecuteNonQueryAsync();}
  using var f=new Factory(cs);using var tech=f.Client();using var admin=f.Client(user:f.Admin);using var stranger=f.Client(user:Guid.NewGuid());Guid provider=Guid.Empty;
  const string prefix="/api/providers/me/verification-documents";
  async Task<VerificationDocumentDetails> Detail(string type)=>await Payload<VerificationDocumentDetails>(await tech.GetAsync(prefix+"/"+type));
  async Task<VerificationDocumentDetails> Upload(string type,string contents,string? name=null)
  {using var form=new MultipartFormDataContent();if(type=="IdentityCheck")form.Add(new StringContent("Passport"),"identityKind");form.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n"+contents)),"file",name??type+".pdf");return await Payload<VerificationDocumentDetails>(await tech.PostAsync(prefix+"/"+type+"/files",form));}
  async Task Submit(string type,Guid id)=>await Payload<VerificationDocumentDetails>(await tech.PostAsJsonAsync(prefix+"/"+type+"/submit",new{documentId=id}));
  async Task<VerificationDocumentDetails> Review(string type,Guid id,bool approved,string? feedback=null)=>await Payload<VerificationDocumentDetails>(await admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/{type}/review",new{documentId=id,approved,feedback}));
  try
  {
   var created=await tech.PostAsJsonAsync("/api/providers/me",new{displayName="Independent reviews technician",categoryIds=new[]{1}});Assert.Equal(HttpStatusCode.OK,created.StatusCode);provider=(await created.Content.ReadFromJsonAsync<ProviderAccount>())!.ProviderId;
   var overview=await Payload<VerificationOverview>(await tech.GetAsync(prefix));Assert.Equal(3,overview.Documents.Count);Assert.All(overview.Documents,x=>Assert.Equal("NotUploaded",x.Status));Assert.Equal(3,overview.Documents.Count(d=>d.RequiredForOrders));
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   var certificate=await Upload("Certificate","certificate-v1");var certId=certificate.Document.CurrentDocument!.DocumentId;
   var business=await Upload("IdentityCheck","business-v1");var businessId=business.Document.CurrentDocument!.DocumentId;Assert.Equal("Draft",business.Document.Status);Assert.False(business.Document.IsVerified);
   var same=await Upload("IdentityCheck","business-v1");Assert.Equal(businessId,same.Document.CurrentDocument!.DocumentId);
   var insurance=await Upload("Insurance","insurance-v1");var insuranceId=insurance.Document.CurrentDocument!.DocumentId;
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/Insurance/review",new{documentId=insuranceId,approved=true})).StatusCode);
   // Invalid bulk submission must not partially submit the valid first document.
   Assert.Equal(HttpStatusCode.Conflict,(await tech.PostAsJsonAsync(prefix+"/submit",new{documentIds=new[]{businessId,Guid.NewGuid()}})).StatusCode);Assert.Equal("Draft",(await Detail("IdentityCheck")).Document.Status);
   await Payload<VerificationOverview>(await tech.PostAsJsonAsync(prefix+"/submit",new{documentIds=new[]{businessId,insuranceId}}));
   await Payload<VerificationOverview>(await tech.PostAsJsonAsync(prefix+"/submit",new{documentIds=new[]{businessId,insuranceId}}));
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/Insurance/review",new{documentId=insuranceId,approved=true})).StatusCode);
   await Review("IdentityCheck",businessId,true);Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   var rejected=await Review("Insurance",insuranceId,false,"Upload a clearer copy.");Assert.Equal("ActionRequired",rejected.Document.Status);Assert.False(rejected.Document.IsVerified);
   Assert.Equal(HttpStatusCode.Conflict,(await tech.PostAsJsonAsync(prefix+"/Insurance/submit",new{documentId=insuranceId})).StatusCode);
   insurance=await Upload("Insurance","insurance-v2");var approvedInsurance=insurance.Document.CurrentDocument!.DocumentId;await Submit("Insurance",approvedInsurance);await Review("Insurance",approvedInsurance,true);await Review("Insurance",approvedInsurance,true);
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   await Submit("Certificate",certId);await Review("Certificate",certId,true);
   Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   var profile=await Payload<VerificationProfile>(await tech.PutAsJsonAsync(prefix+"/profile",new{postcode="sw1a 1aa",otherSkills="Painting"}));Assert.Equal("SW1A 1AA",profile.Postcode);
   Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/quote-profile?categoryId=1")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await tech.GetAsync("/api/providers/me/quote-profile?categoryId=2")).StatusCode);
   // Physical file URLs stay private, and previous versions remain downloadable.
   var download=$"/api/providers/me/documents/{insuranceId}";Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync(download)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync(download)).StatusCode);
   var replacement=await Upload("Insurance","insurance-v3");var replacementId=replacement.Document.CurrentDocument!.DocumentId;Assert.True(replacement.Document.IsVerified);Assert.Equal("Draft",replacement.Document.Status);Assert.Equal(approvedInsurance,replacement.Document.VerifiedDocument!.DocumentId);
   Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   await Submit("Insurance",replacementId);var rejectedReplacement=await Review("Insurance",replacementId,false,"The replacement is incomplete.");Assert.True(rejectedReplacement.Document.IsVerified);Assert.Equal(approvedInsurance,rejectedReplacement.Document.VerifiedDocument!.DocumentId);Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   var superseded=await Upload("Insurance","insurance-v4");var oldPending=superseded.Document.CurrentDocument!.DocumentId;await Submit("Insurance",oldPending);
   var latest=await Upload("Insurance","insurance-v5");var latestId=latest.Document.CurrentDocument!.DocumentId;
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/Insurance/review",new{documentId=oldPending,approved=true})).StatusCode);
   var queue=await Payload<PendingVerificationPage>(await admin.GetAsync("/api/admin/verification-documents"));Assert.DoesNotContain(queue.Items,x=>x.Document.DocumentId==oldPending);
   await Submit("Insurance",latestId);
   var races=await Task.WhenAll(admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/Insurance/review",new{documentId=latestId,approved=true}),admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/Insurance/review",new{documentId=latestId,approved=false,feedback="Conflicting decision"}));
   Assert.Single(races,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(races,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var result in races)result.Dispose();
   Assert.True((await Detail("Insurance")).Document.IsVerified);
   var certReplacement=await Upload("Certificate","certificate-v2");var certReplacementId=certReplacement.Document.CurrentDocument!.DocumentId;await Submit("Certificate",certReplacementId);await Review("Certificate",certReplacementId,false,"Replace the certificate.");
   overview=await Payload<VerificationOverview>(await tech.GetAsync(prefix));Assert.Equal(3,overview.Documents.Count(d=>d.IsVerified));Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification",new{revision=1,approved=true})).StatusCode);
   // Changing business identity revokes only that approval, never Insurance.
   Assert.Equal(HttpStatusCode.OK,(await tech.PutAsJsonAsync("/api/providers/me",new{displayName="Changed business identity",categoryIds=new[]{1}})).StatusCode);
   Assert.Equal("ActionRequired",(await Detail("IdentityCheck")).Document.Status);Assert.False((await Detail("IdentityCheck")).Document.IsVerified);Assert.True((await Detail("Insurance")).Document.IsVerified);Assert.Equal(HttpStatusCode.Forbidden,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsJsonAsync($"/api/admin/providers/{provider}/verification-documents/IdentityCheck/review",new{documentId=businessId,approved=true})).StatusCode);
   var newBusiness=await Upload("IdentityCheck","business-v1");var newBusinessId=newBusiness.Document.CurrentDocument!.DocumentId;Assert.NotEqual(businessId,newBusinessId);await Submit("IdentityCheck",newBusinessId);await Review("IdentityCheck",newBusinessId,true);
   Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   // A legacy raw upload must not revoke independent approvals or rely on a global revision.
   using(var legacy=new MultipartFormDataContent()){legacy.Add(new StringContent("Other"),"kind");legacy.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nlegacy")),"file","legacy.pdf");Assert.Equal(HttpStatusCode.OK,(await tech.PostAsync("/api/providers/me/documents",legacy)).StatusCode);}
   Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync("/api/providers/me/eligibility")).StatusCode);
   var history=await Payload<IReadOnlyList<DocumentReviewAudit>>(await admin.GetAsync($"/api/admin/providers/{provider}/verification-documents/history"));Assert.Contains(history,x=>x.EventType=="IdentityDetailsChanged");Assert.Equal(2,history.Count(x=>x.EventType=="Submitted"&&(x.DocumentId==businessId||x.DocumentId==insuranceId)));Assert.Equal(1,history.Count(x=>x.EventType=="Verified"&&x.DocumentId==approvedInsurance));
   var detail=await Detail("Insurance");Assert.True(detail.History.Count>=5);
  }
  finally
  {
   if(provider!=Guid.Empty){await using var cleanup=new MySqlCommand("DELETE FROM provider_document_review_history WHERE provider_id=@id;DELETE FROM provider_verification_profile WHERE provider_id=@id;DELETE FROM provider_verification_slots WHERE provider_id=@id;DELETE FROM provider_document_reviews WHERE provider_id=@id;DELETE FROM provider_documents WHERE provider_id=@id;DELETE FROM provider_onboarding WHERE provider_id=@id;DELETE FROM provider_category_qualifications WHERE provider_id=@id;DELETE FROM providers WHERE id=@id;",db);cleanup.Parameters.AddWithValue("@id",provider.ToString());await cleanup.ExecuteNonQueryAsync();}
  }
 }
 private class Factory(string? cs=null):WebApplicationFactory<Program>
 {
  const string Key="document-review-test-only-key-12345678901234567890";public Guid User=Guid.NewGuid(),Admin=Guid.NewGuid();public Session Session=new();
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {
   b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"},{"ConnectionStrings:DefaultConnection",cs??"Server=127.0.0.1;Database=unused;User=test"}}));
   b.ConfigureServices(s=>{s.RemoveAll<IProviderWorkflowClient>();s.AddSingleton<IProviderWorkflowClient>(new AdminClient(Admin));s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator>(Session);});
  }
  public HttpClient Client(bool authenticated=true,Guid? user=null)
  {var c=CreateClient();if(authenticated){var token=new JwtSecurityToken("tests","clients",new[]{new Claim("sub",(user??User).ToString()),new Claim("sid",Guid.NewGuid().ToString())},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(token));}return c;}
 }
 private class Session:IIdentitySessionValidator{public bool Active=true;public Task<bool> IsActiveAsync(string t,bool h,CancellationToken ct)=>Task.FromResult(Active);}
 private class AdminClient(Guid admin):IProviderWorkflowClient
 {
  public Task<bool> IsAdminAsync(string token,CancellationToken ct)=>Task.FromResult(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject==admin.ToString());
  public Task<CompletedWork> CompletedWorkAsync(Guid id,string t,CancellationToken ct)=>throw new NotImplementedException();
  public Task<int?> CompletedJobsAsync(Guid id,string t,CancellationToken ct)=>Task.FromResult<int?>(0);
 }
}
