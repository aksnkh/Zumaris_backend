using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Infrastructure.Persistence;
using Xunit;
namespace Provider.Tests;
[Collection("Provider MySQL")]
public class MySqlProviderSignupTests
{
 [ProviderMySqlFact]
 public async Task Concurrent_signup_preserves_one_consent_record_and_never_declares_a_trade_or_verifies_the_provider()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_provider_test",parsed.Database);
  await using var c=new MySqlConnection(cs);await c.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  foreach(var file in new[]{"001_provider_quote_profiles.sql","002_onboarding_reviews.sql","003_signup.sql","004_independent_document_reviews.sql"})
  for(int i=0;i<2;i++){await using var cmd=new MySqlCommand((await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider",file))).Replace("zumaris_provider","zumaris_provider_test"),c);await cmd.ExecuteNonQueryAsync();}
  var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();var accounts=new ProviderAccountRepository(cfg);var repo=new ProviderSignupRepository(cfg,accounts);var user=Guid.NewGuid();Guid id=Guid.Empty;
  try
  {
   var results=await Task.WhenAll(repo.CompleteAsync(user,"John Smith","test-terms","test-privacy",default),repo.CompleteAsync(user,"John Smith","test-terms","test-privacy",default));
   id=results[0].Profile.ProviderId;Assert.Equal(id,results[1].Profile.ProviderId);Assert.Equal(results[0].TermsAndPrivacyAcceptedAtUtc,results[1].TermsAndPrivacyAcceptedAtUtc);Assert.Equal("Draft",results[0].Profile.OnboardingStatus);Assert.False(results[0].Profile.IsVerified);Assert.Empty(results[0].Profile.CategoryIds);Assert.Empty(results[0].Profile.Documents);
   Assert.Null(await new ProviderQuoteRepository(cfg).GetAsync(user,null,1,default));
   await using(var check=new MySqlCommand("SELECT COUNT(*) FROM provider_signup_details WHERE provider_id=@id",c)){check.Parameters.AddWithValue("@id",id.ToString());Assert.Equal(1,Convert.ToInt32(await check.ExecuteScalarAsync()));}
   var repeat=await repo.CompleteAsync(user,"Different display","changed-terms","changed-privacy",default);Assert.Equal("John Smith",repeat.Profile.DisplayName);Assert.Equal("test-terms",repeat.TermsVersion);Assert.Equal(results[0].TermsAndPrivacyAcceptedAtUtc,repeat.TermsAndPrivacyAcceptedAtUtc);
   var updated=await accounts.SaveAsync(user,new("John Smith",new[]{1,2}),default);Assert.Contains(1,updated.CategoryIds);Assert.False(updated.IsVerified);
  }
  finally
  {if(id!=Guid.Empty){await using var cleanup=new MySqlCommand("DELETE FROM provider_signup_details WHERE provider_id=@id;DELETE FROM provider_category_qualifications WHERE provider_id=@id;DELETE FROM provider_onboarding WHERE provider_id=@id;DELETE FROM providers WHERE id=@id",c);cleanup.Parameters.AddWithValue("@id",id.ToString());await cleanup.ExecuteNonQueryAsync();}}
 }
}
