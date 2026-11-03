using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
namespace Identity.Tests;
public class MySqlAdminTests
{
 [IdentityMySqlFact]
 public async Task Platform_administrator_membership_is_server_owned_and_revocation_is_immediate()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_identity_test",parsed.Database);
  await using var c=new MySqlConnection(cs);await c.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/004_platform_administrators.sql"))).Replace("zumaris_identity","zumaris_identity_test");
  for(int i=0;i<2;i++){await using var setup=new MySqlCommand(sql,c);await setup.ExecuteNonQueryAsync();}
  var repo=new PlatformAdminRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build());var user=Guid.NewGuid();
  try
  {
   Assert.False(await repo.IsAdminAsync(user,default));
   await using(var cmd=new MySqlCommand("INSERT INTO platform_administrators(user_id)VALUES(@id)",c)){cmd.Parameters.AddWithValue("@id",user.ToString());await cmd.ExecuteNonQueryAsync();}
   Assert.True(await repo.IsAdminAsync(user,default));
   await using(var cmd=new MySqlCommand("UPDATE platform_administrators SET is_active=0 WHERE user_id=@id",c)){cmd.Parameters.AddWithValue("@id",user.ToString());await cmd.ExecuteNonQueryAsync();}
   Assert.False(await repo.IsAdminAsync(user,default));
  }
  finally{await using var cmd=new MySqlCommand("DELETE FROM platform_administrators WHERE user_id=@id",c);cmd.Parameters.AddWithValue("@id",user.ToString());await cmd.ExecuteNonQueryAsync();}
 }
}
