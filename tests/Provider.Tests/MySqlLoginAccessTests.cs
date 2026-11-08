using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
using Zumaris.Provider.Infrastructure.Persistence;
namespace Provider.Tests;
[Collection("Provider MySQL")]
public class MySqlLoginAccessTests
{
    [ProviderMySqlFact]
    public async Task Login_requires_active_consented_signup_but_not_verified_documents()
    {
        var cs=Environment.GetEnvironmentVariable("ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION")!;
        var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_provider_test",parsed.Database);
        await using var db=new MySqlConnection(cs);await db.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
        foreach(var file in new[]{"001_provider_quote_profiles.sql","003_signup.sql"})
        {await using var migration=new MySqlCommand((await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/provider",file))).Replace("zumaris_provider","zumaris_provider_test"),db);await migration.ExecuteNonQueryAsync();}
        var repo=new ProviderLoginAccessRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build());
        var user=Guid.NewGuid();var provider=Guid.NewGuid();Assert.Null(await repo.GetAsync(user,default));
        async Task Run(string sql){await using var cmd=new MySqlCommand(sql,db);cmd.Parameters.AddWithValue("@user",user.ToString());cmd.Parameters.AddWithValue("@id",provider.ToString());await cmd.ExecuteNonQueryAsync();}
        try
        {
            await Run("INSERT INTO providers(id,user_id,display_name)VALUES(@id,@user,'Unverified technician')");
            var draft=await repo.GetAsync(user,default);Assert.NotNull(draft);Assert.True(draft.IsActive);Assert.False(draft.HasSignedUp);
            await Run("INSERT INTO provider_signup_details(provider_id,accepted_at)VALUES(@id,UTC_TIMESTAMP(6))");
            var ready=await repo.GetAsync(user,default);Assert.NotNull(ready);Assert.True(ready.IsActive);Assert.True(ready.HasSignedUp);Assert.Equal(provider,ready.ProviderId);Assert.Equal(user,ready.UserId);
            await Run("UPDATE providers SET is_active=0 WHERE id=@id");Assert.False((await repo.GetAsync(user,default))!.IsActive);
        }
        finally {await Run("DELETE FROM provider_signup_details WHERE provider_id=@id;DELETE FROM providers WHERE id=@id;");}
    }
}
