using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application.Quotes;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;
public sealed class QuoteMySqlFactAttribute:FactAttribute
{
    public QuoteMySqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_QUOTES_MYSQL_TEST_CONNECTION")))Skip="Requires an isolated loopback zumaris_quotes_test MySQL database.";}
}
[Collection("Request MySQL")]
public class MySqlQuoteTests
{
    private static async Task<QuoteDto> Quote(HttpResponseMessage response,HttpStatusCode expected=HttpStatusCode.OK)
    {
        Assert.Equal(expected,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("payload").Deserialize<QuoteDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [QuoteMySqlFact]
    public async Task Quote_workflow_enforces_ownership_qualification_prices_idempotency_and_atomic_assignment()
    {
        var cs=Environment.GetEnvironmentVariable("ZUMARIS_QUOTES_MYSQL_TEST_CONNECTION")!;var builder=new MySqlConnectionStringBuilder(cs);
        Assert.Contains(builder.Server,new[]{"localhost","127.0.0.1","::1"});Assert.Equal("zumaris_quotes_test",builder.Database);
        await using var c=new MySqlConnection(cs);await c.OpenAsync();
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
        foreach(var file in new[]{"001_service_requests.sql","002_request_lifecycle.sql","003_service_history.sql","005_service_request_quotes.sql"})
        {
            if(file=="003_service_history.sql")
            {
                await using var exists=new MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='service_requests' AND column_name='request_number'",c);
                if(Convert.ToInt32(await exists.ExecuteScalarAsync())>0)continue;
            }
            var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/service-request",file))).Replace("zumaris_service_request","zumaris_quotes_test");
            // This fact uses a fresh dedicated database; it is not a migration runner for user databases.
            await using var cmd=new MySqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();
        }
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
        using var f=new RequestFactory{QuoteRepositoryOverride=new QuoteAdoNetRepository(config)};using var owner=f.Client();
        var tech1=Guid.NewGuid();var tech2=Guid.NewGuid();var stranger=Guid.NewGuid();
        f.QuoteProvider.Users[tech1]=new(Guid.NewGuid(),tech1,"Technician One",null,null,null,true,1);
        f.QuoteProvider.Users[tech2]=new(Guid.NewGuid(),tech2,"Technician Two",null,4.9m,120,true,1);
        using var one=f.Client(userId:tech1,application:"Tech");using var two=f.Client(userId:tech2,application:"Tech");using var other=f.Client(userId:stranger);
        var requestIds=new List<Guid>();
        async Task<Guid> Request()
        {
            var id=Guid.NewGuid();requestIds.Add(id);
            await using var cmd=new MySqlCommand("""
                INSERT INTO service_requests(id,user_id,property_id,category_id,area_type_id,description,urgency,time_zone_id,status,created_at,request_hash,property_address)
                VALUES(@id,@user,@property,1,1,'The kitchen tap is leaking and needs repair.','Emergency','Europe/London','Pending','2029-01-01',REPEAT('a',64),'12 Example Road')
                """,c);cmd.Parameters.AddWithValue("@id",id.ToString());cmd.Parameters.AddWithValue("@user",f.UserId.ToString());cmd.Parameters.AddWithValue("@property",Guid.NewGuid().ToString());await cmd.ExecuteNonQueryAsync();return id;
        }
        var input=new{labourAmount=75,calloutAmount=20,currencyCode="GBP",providerNote="Parts may be required."};
        try
        {
            var id=await Request();var path=$"/api/service-requests/{id}/quotes";
            Assert.Equal(HttpStatusCode.Forbidden,(await other.PostAsJsonAsync(path,input)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(path,input)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await one.GetAsync($"/api/service-requests/{id}/quote-context")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync($"/api/service-requests/{id}/quote-context")).StatusCode);
            var first=await Quote(await one.PostAsJsonAsync(path,input),HttpStatusCode.Created);Assert.Equal(95m,first.TotalAmount);Assert.Null(first.ProviderRating);Assert.Null(first.PropertyAddress);
            Assert.Equal("12 Example Road",(await Quote(await owner.GetAsync($"/api/quotes/{first.Id}"))).PropertyAddress);
            using var providerInApp=f.Client(userId:tech1,application:"App");using var customerInTech=f.Client(userId:f.UserId,application:"Tech");
            Assert.Equal(HttpStatusCode.NotFound,(await providerInApp.GetAsync($"/api/quotes/{first.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await customerInTech.GetAsync($"/api/quotes/{first.Id}")).StatusCode);
            Assert.Equal(first.Id,(await Quote(await one.PostAsJsonAsync(path,input),HttpStatusCode.Created)).Id);
            Assert.Equal(first.Id,(await Quote(await one.PostAsJsonAsync(path,new{labourAmount=75.00m,calloutAmount=20.00m,currencyCode="GBP",providerNote="Parts may be required."}),HttpStatusCode.Created)).Id);
            Assert.Equal(HttpStatusCode.Conflict,(await one.PostAsJsonAsync(path,new{labourAmount=80,calloutAmount=20})).StatusCode);
            var second=await Quote(await two.PostAsJsonAsync(path,input),HttpStatusCode.Created);
            Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/quotes/{first.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/api/quotes/{first.Id}/accept",null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await one.PostAsync($"/api/quotes/{first.Id}/accept",null)).StatusCode);
            using(var list=JsonDocument.Parse(await owner.GetStringAsync("/api/quotes?status=Pending")))Assert.Equal(2,list.RootElement.GetProperty("payload").GetProperty("totalCount").GetInt64());
            var accepted=await Quote(await owner.PostAsync($"/api/quotes/{first.Id}/accept",null));Assert.Equal("Accepted",accepted.Status);
            Assert.Equal("Accepted",(await Quote(await owner.PostAsync($"/api/quotes/{first.Id}/accept",null))).Status);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsync($"/api/quotes/{second.Id}/accept",null)).StatusCode);
            Assert.Equal("Withdrawn",(await Quote(await owner.GetAsync($"/api/quotes/{second.Id}"))).Status);
            await using(var state=new MySqlCommand("SELECT status,technician_id,payment_status,total_amount FROM service_requests WHERE id=@id",c))
            {
                state.Parameters.AddWithValue("@id",id.ToString());await using var r=await state.ExecuteReaderAsync();Assert.True(await r.ReadAsync());
                Assert.Equal("Assigned",r.GetString(0));Assert.Equal(first.ProviderId,r.GetGuid(1));Assert.Equal("Unpaid",r.GetString(2));Assert.True(r.IsDBNull(3));
            }
            await using(var count=new MySqlCommand("SELECT COUNT(*) FROM service_request_status_history WHERE request_id=@id AND to_status='Assigned'",c))
            {count.Parameters.AddWithValue("@id",id.ToString());Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));}
            var declinedId=await Request();var decline=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{declinedId}/quotes",input),HttpStatusCode.Created);
            Assert.Equal("Declined",(await Quote(await owner.PostAsync($"/api/quotes/{decline.Id}/decline",null))).Status);
            Assert.Equal("Declined",(await Quote(await owner.PostAsync($"/api/quotes/{decline.Id}/decline",null))).Status);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsync($"/api/quotes/{decline.Id}/accept",null)).StatusCode);
            var staleId=await Request();var stale=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{staleId}/quotes",input),HttpStatusCode.Created);
            await using(var update=new MySqlCommand("UPDATE service_requests SET version=version+1 WHERE id=@id",c)){update.Parameters.AddWithValue("@id",staleId.ToString());await update.ExecuteNonQueryAsync();}
            Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsync($"/api/quotes/{stale.Id}/accept",null)).StatusCode);
            var expiryId=await Request();var expiring=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{expiryId}/quotes",new{labourAmount=75,calloutAmount=20,expiresAtUtc=f.Clock.Now.UtcDateTime.AddMinutes(1)}),HttpStatusCode.Created);
            f.Clock.Now=f.Clock.Now.AddMinutes(2);Assert.Equal("Expired",(await Quote(await owner.GetAsync($"/api/quotes/{expiring.Id}"))).Status);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsync($"/api/quotes/{expiring.Id}/accept",null)).StatusCode);
            var raceId=await Request();var race1=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{raceId}/quotes",input),HttpStatusCode.Created);
            var race2=await Quote(await two.PostAsJsonAsync($"/api/service-requests/{raceId}/quotes",input),HttpStatusCode.Created);
            var responses=await Task.WhenAll(owner.PostAsync($"/api/quotes/{race1.Id}/accept",null),owner.PostAsync($"/api/quotes/{race2.Id}/accept",null));
            Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);
            var withdrawalId=await Request();var withdrawal=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{withdrawalId}/quotes",input),HttpStatusCode.Created);
            Assert.Equal("Withdrawn",(await Quote(await one.PostAsync($"/api/quotes/{withdrawal.Id}/withdraw",null))).Status);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsync($"/api/quotes/{withdrawal.Id}/accept",null)).StatusCode);
            var qualificationId=await Request();var qualification=await Quote(await one.PostAsJsonAsync($"/api/service-requests/{qualificationId}/quotes",input),HttpStatusCode.Created);
            var validProfile=f.QuoteProvider.Users[tech1];f.QuoteProvider.Users[tech1]=validProfile with {IsVerified=false};
            Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsync($"/api/quotes/{qualification.Id}/accept",null)).StatusCode);
            f.QuoteProvider.Users[tech1]=validProfile;
            var referenceId=await Request();
            await using(var snapshot=new MySqlCommand("UPDATE service_requests SET category_name='Old category',service_area='Old area',service_name='Old service',category_icon='old.svg' WHERE id=@id",c))
            {snapshot.Parameters.AddWithValue("@id",referenceId.ToString());await snapshot.ExecuteNonQueryAsync();}
            var lifecycle=new RequestLifecycleAdoNetRepository(config);var historyRepository=new ServiceHistoryAdoNetRepository(config);
            var newInput=RequestFactory.Valid() with {CategoryId=2,AreaTypeId=3};
            await lifecycle.UpdateAsync(f.UserId,referenceId,newInput,1,f.Clock.Now.UtcDateTime,default);
            var reference=await historyRepository.GetByIdAsync(f.UserId,referenceId,default);
            Assert.Equal(2,reference!.CategoryId);Assert.Equal(3,reference.AreaTypeId);Assert.Equal(newInput.PropertyId,reference.PropertyId);
            Assert.Equal(newInput.PreferredDate,reference.PreferredDate);Assert.Equal(newInput.PreferredStartTime,reference.PreferredStartTime);
            Assert.Equal(newInput.PreferredEndTime,reference.PreferredEndTime);Assert.Equal("Europe/London",reference.TimeZoneId);
            Assert.Null(reference.CategoryName);Assert.Null(reference.ServiceArea);Assert.Null(reference.ServiceName);Assert.Null(reference.PropertyAddress);
            var historyPage=await historyRepository.GetHistoryAsync(f.UserId,new(),default);
            var projected=Assert.Single(historyPage.Items,i=>i.RequestId==referenceId);
            Assert.Equal(2,projected.CategoryId);Assert.Equal(3,projected.AreaTypeId);Assert.Equal(newInput.PropertyId,projected.PropertyId);
            Assert.Equal(newInput.PreferredDate,projected.PreferredDate);Assert.Equal(newInput.PreferredStartTime,projected.PreferredStartTime);
            var failedId=await Request();f.QuoteProvider.Unavailable=true;
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await one.PostAsJsonAsync($"/api/service-requests/{failedId}/quotes",input)).StatusCode);
        }
        finally
        {
            foreach(var id in requestIds)
            {
                await using var cleanup=new MySqlCommand("DELETE FROM service_request_quotes WHERE request_id=@id; DELETE FROM service_request_status_history WHERE request_id=@id; DELETE FROM service_requests WHERE id=@id;",c);
                cleanup.Parameters.AddWithValue("@id",id.ToString());await cleanup.ExecuteNonQueryAsync();
            }
        }
    }
}
