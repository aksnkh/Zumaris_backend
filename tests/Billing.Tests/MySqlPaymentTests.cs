using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Billing.Application;
using Zumaris.Billing.Infrastructure;
using Xunit;
namespace Billing.Tests;
public sealed class BillingMySqlFactAttribute:FactAttribute
{public BillingMySqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_BILLING_MYSQL_TEST_CONNECTION")))Skip="Requires isolated loopback zumaris_billing_test database.";}}
public class MySqlPaymentTests
{
 [BillingMySqlFact]
 public async Task Concurrent_retries_create_one_fixed_price_payment_intent_without_a_charge()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_BILLING_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_billing_test",parsed.Database);
  await using var c=new MySqlConnection(cs);await c.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/billing/001_payment_intents.sql"))).Replace("zumaris_billing","zumaris_billing_test");
  for(int i=0;i<2;i++){await using var setup=new MySqlCommand(sql,c);await setup.ExecuteNonQueryAsync();}
  var repo=new PaymentRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build());
  var user=Guid.NewGuid();var work=new CompletedWork(Guid.NewGuid(),user,Guid.NewGuid(),"Completed",Guid.NewGuid(),95,"GBP");
  try
  {
   var results=await Task.WhenAll(repo.CreateAsync(user,work,default),repo.CreateAsync(user,work,default));Assert.Equal(results[0].Id,results[1].Id);Assert.False(results[0].CanPay);Assert.Equal("AwaitingGateway",results[0].Status);
   Assert.Null(await repo.GetAsync(Guid.NewGuid(),results[0].Id,default));
   var error=await Assert.ThrowsAsync<BillingException>(()=>repo.CreateAsync(user,work with{Amount=1},default));Assert.Equal(409,error.Status);
   Assert.Equal(95,(await repo.GetAsync(user,results[0].Id,default))!.Amount);
  }
  finally{await using var cmd=new MySqlCommand("DELETE FROM payment_intents WHERE request_id=@id",c);cmd.Parameters.AddWithValue("@id",work.RequestId.ToString());await cmd.ExecuteNonQueryAsync();}
 }
}
