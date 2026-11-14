using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Application.Quotes;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;
[Collection("Request MySQL")]
public class TechnicianWorkflowTests
{
 [Fact]
 public async Task Swagger_documents_technician_execution_and_customer_completion_context()
 {
  using var f=new RequestFactory();using var c=f.Client();var json=await c.GetStringAsync("/swagger/v1/swagger.json");
  Assert.Contains("/api/technician/opportunities",json);Assert.Contains("/api/technician/jobs/{id}/complete",json);Assert.Contains("/api/service-requests/{id}/completed-work",json);Assert.Contains("multipart/form-data",json);
 }

 [QuoteMySqlFact]
 public async Task Opportunity_assignment_execution_completion_and_result_photo_access_follow_the_order_workflow()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_QUOTES_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_quotes_test",parsed.Database);
  await using var db=new MySqlConnection(cs);await db.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  foreach(var file in new[]{"001_service_requests.sql","002_request_lifecycle.sql","003_service_history.sql","005_service_request_quotes.sql","006_technician_execution.sql"})
  {
   if(file=="003_service_history.sql"){await using var exists=new MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='service_requests' AND column_name='request_number'",db);if(Convert.ToInt32(await exists.ExecuteScalarAsync())>0)continue;}
   await using var cmd=new MySqlCommand((await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/service-request",file))).Replace("zumaris_service_request","zumaris_quotes_test"),db);await cmd.ExecuteNonQueryAsync();
  }
  var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  using var f=new RequestFactory{QuoteRepositoryOverride=new QuoteAdoNetRepository(cfg),WorkRepositoryOverride=new TechnicianWorkRepository(cfg)};
  var technician=Guid.NewGuid();var stranger=Guid.NewGuid();var provider=Guid.NewGuid();f.QuoteProvider.Users[technician]=new(provider,technician,"Test technician",null,null,null,true,1);f.WorkEligibility.Categories[technician]=new[]{1};f.WorkEligibility.Categories[stranger]=new[]{2};
  using var owner=f.Client();using var tech=f.Client(userId:technician,application:"Tech");using var other=f.Client(userId:stranger,application:"Tech");using var anonymous=f.Client(false);
  var id=Guid.NewGuid();
  await using(var insert=new MySqlCommand("""
   INSERT INTO service_requests(id,user_id,property_id,category_id,area_type_id,description,urgency,time_zone_id,status,created_at,request_hash)
   VALUES(@id,@user,@property,1,1,'The kitchen tap is leaking and needs repair.','Emergency','Europe/London','Pending','2029-01-01',REPEAT('a',64))
   """,db)){insert.Parameters.AddWithValue("@id",id.ToString());insert.Parameters.AddWithValue("@user",f.UserId.ToString());insert.Parameters.AddWithValue("@property",Guid.NewGuid().ToString());await insert.ExecuteNonQueryAsync();}
  try
  {
   Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/technician/opportunities")).StatusCode);
   Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/technician/opportunities")).StatusCode);
   var opportunities=await tech.GetFromJsonAsync<OpportunityPage>("/api/technician/opportunities");Assert.Contains(opportunities!.Items,x=>x.RequestId==id);
   var unrelated=await other.GetFromJsonAsync<OpportunityPage>("/api/technician/opportunities");Assert.DoesNotContain(unrelated!.Items,x=>x.RequestId==id);
   Assert.Equal(HttpStatusCode.NotFound,(await tech.GetAsync($"/api/technician/jobs/{id}")).StatusCode);
   var sent=await tech.PostAsJsonAsync($"/api/service-requests/{id}/quotes",new{labourAmount=75,calloutAmount=20});Assert.Equal(HttpStatusCode.Created,sent.StatusCode);
   using var quoteJson=JsonDocument.Parse(await sent.Content.ReadAsStringAsync());var quoteId=quoteJson.RootElement.GetProperty("payload").GetProperty("id").GetGuid();
   Assert.Equal(HttpStatusCode.OK,(await owner.PostAsync($"/api/quotes/{quoteId}/accept",null)).StatusCode);
   Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync($"/api/service-requests/{id}/completed-work")).StatusCode);
   Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsync($"/api/technician/jobs/{id}/start",null)).StatusCode);
   Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/api/technician/jobs/{id}/start",null)).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await tech.PostAsync($"/api/technician/jobs/{id}/start",null)).StatusCode);
   Assert.Equal(HttpStatusCode.OK,(await tech.PostAsync($"/api/technician/jobs/{id}/start",null)).StatusCode);
   using(var noPhotos=new MultipartFormDataContent()){noPhotos.Add(new StringContent("Replaced the leaking seal."),"report");var bad=await tech.PostAsync($"/api/technician/jobs/{id}/complete",noPhotos);Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);using var body=JsonDocument.Parse(await bad.Content.ReadAsStringAsync());Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));}
   using(var invalid=new MultipartFormDataContent()){invalid.Add(new StringContent("Replaced the leaking seal."),"report");invalid.Add(new ByteArrayContent(new byte[]{1,2,3}),"photos","invalid.png");Assert.Equal(HttpStatusCode.BadRequest,(await tech.PostAsync($"/api/technician/jobs/{id}/complete",invalid)).StatusCode);}
   Assert.Equal("InProgress",(await tech.GetFromJsonAsync<TechnicianJob>($"/api/technician/jobs/{id}"))!.Status);
   using var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.Red,10,10);var bytes=image.ToByteArray(ImageMagick.MagickFormat.Png);
   async Task<HttpStatusCode> Complete()
   {using var form=new MultipartFormDataContent();form.Add(new StringContent("Replaced the leaking seal and tested the tap."),"report");form.Add(new ByteArrayContent(bytes),"photos","result.png");using var response=await tech.PostAsync($"/api/technician/jobs/{id}/complete",form);return response.StatusCode;}
   var decisions=await Task.WhenAll(Complete(),Complete());Assert.Single(decisions,x=>x==HttpStatusCode.OK);Assert.Single(decisions,x=>x==HttpStatusCode.Conflict);
   Assert.Single(Directory.GetFiles(f.UploadRoot,"*",SearchOption.AllDirectories));
   var execution=await owner.GetFromJsonAsync<TechnicianJob>($"/api/service-requests/{id}/execution");Assert.Equal("Completed",execution!.Status);Assert.NotNull(execution.CompletedAtUtc);Assert.NotNull(execution.StartedAtUtc);Assert.NotEmpty(execution.WorkReport!);var photo=Assert.Single(execution.ResultPhotos);
   var photoPath=photo.DownloadPath.Replace("/service-request","");Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync(photoPath)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await tech.GetAsync(photoPath)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(photoPath)).StatusCode);
   Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync($"/api/service-requests/{id}/completed-work")).StatusCode);
   using(var techInApp=f.Client(userId:technician,application:"App"))Assert.Equal(HttpStatusCode.NotFound,(await techInApp.GetAsync(photoPath)).StatusCode);
   using(var ownerInTech=f.Client(userId:f.UserId,application:"Tech"))Assert.Equal(HttpStatusCode.NotFound,(await ownerInTech.GetAsync(photoPath)).StatusCode);
   var work=await owner.GetFromJsonAsync<CompletedWorkContext>($"/api/service-requests/{id}/completed-work");Assert.Equal(95,work!.Amount);Assert.Equal(quoteId,work.QuoteId);Assert.Equal(provider,work.ProviderId);
   var history=await new ServiceHistoryAdoNetRepository(cfg).GetByIdAsync(f.UserId,id,default);Assert.Equal("Completed",history!.Status);Assert.Equal("Unpaid",history.PaymentStatus);Assert.Equal(95,history.TotalAmount);Assert.NotNull(history.ServiceDate);Assert.NotNull(history.CompletedAt);
   await using var count=new MySqlCommand("SELECT COUNT(*) FROM service_request_status_history WHERE request_id=@id AND to_status='InProgress'",db);count.Parameters.AddWithValue("@id",id.ToString());Assert.Equal(1,Convert.ToInt32(await count.ExecuteScalarAsync()));
  }
  finally
  {
   await using var cleanup=new MySqlCommand("DELETE FROM service_request_result_photos WHERE request_id=@id;DELETE FROM service_request_execution WHERE request_id=@id;DELETE FROM service_request_quotes WHERE request_id=@id;DELETE FROM service_request_status_history WHERE request_id=@id;DELETE FROM service_requests WHERE id=@id;",db);cleanup.Parameters.AddWithValue("@id",id.ToString());await cleanup.ExecuteNonQueryAsync();
  }
 }
}
