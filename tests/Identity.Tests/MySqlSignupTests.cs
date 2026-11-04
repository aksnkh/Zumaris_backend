using Identity.Application.Features.Signup;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
namespace Identity.Tests;
public sealed class SignupMySqlFactAttribute:FactAttribute
{public SignupMySqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_SIGNUP_MYSQL_TEST_CONNECTION")))Skip="Requires isolated loopback zumaris_identity_signup_test database.";}}
public class MySqlSignupTests
{
 [SignupMySqlFact]
 public async Task Account_and_personal_details_are_atomic_duplicate_safe_and_support_email_login_lookup()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_SIGNUP_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_identity_signup_test",parsed.Database);
  await using var c=new MySqlConnection(cs);await c.OpenAsync();
  // Disposable test schema matching the existing Identity/Google-registration columns.
  await using(var setup=new MySqlCommand("""
   CREATE TABLE IF NOT EXISTS identity_users (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    user_name VARCHAR(100) NOT NULL UNIQUE,email VARCHAR(254) NOT NULL,
    password_hash VARCHAR(1024) NULL,is_active BOOLEAN NOT NULL,is_deleted BOOLEAN NOT NULL,created_at DATETIME(6) NOT NULL
   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
   """,c))await setup.ExecuteNonQueryAsync();
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  foreach(var file in new[]{"003_user_profiles.sql","005_signup.sql"})
  for(int i=0;i<2;i++){var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity",file))).Replace("zumaris_identity","zumaris_identity_signup_test");await using var setup=new MySqlCommand(sql,c);await setup.ExecuteNonQueryAsync();}
  var repo=new SignupRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build());
  var hash=new PasswordHasher().Hash("Password-for-signup-test");var email="signup-"+Guid.NewGuid().ToString("N")+"@example.test";var input=new SignupAccount("John","Smith",email,"+447700900123",hash);var ids=new List<Guid>();
  async Task<Guid?> Create(){try{var user=await repo.CreateAsync(input,default);return user.Id;}catch(SignupException ex){Assert.Equal(409,ex.Status);return null;}}
  try
  {
   var outcomes=await Task.WhenAll(Create(),Create());var id=Assert.Single(outcomes,x=>x.HasValue)!.Value;ids.Add(id);
   var user=await repo.FindByEmailAsync(email.ToUpperInvariant(),default);Assert.NotNull(user);Assert.Equal(id,user.Id);Assert.True(new PasswordHasher().Verify("Password-for-signup-test",user.PasswordHash!));Assert.True(user.UserName.Length<=100);
   await using(var profile=new MySqlCommand("SELECT first_name,last_name,phone FROM identity_user_profiles WHERE user_id=@id",c))
   {profile.Parameters.AddWithValue("@id",id.ToString());await using var r=await profile.ExecuteReaderAsync();Assert.True(await r.ReadAsync());Assert.Equal("John",r.GetString(0));Assert.Equal("Smith",r.GetString(1));Assert.Equal("+447700900123",r.GetString(2));}
   var invalidEmail="bad-"+Guid.NewGuid().ToString("N")+"@example.test";
   await Assert.ThrowsAsync<MySqlException>(()=>repo.CreateAsync(input with{Email=invalidEmail,Phone="invalid"},default));
   await using(var check=new MySqlCommand("SELECT (SELECT COUNT(*) FROM identity_users WHERE email=@email)+(SELECT COUNT(*) FROM identity_signup_emails WHERE normalized_email=@email)",c)){check.Parameters.AddWithValue("@email",invalidEmail);Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync()));}
   var legacy=Guid.NewGuid();ids.Add(legacy);var legacyEmail="legacy-"+Guid.NewGuid().ToString("N")+"@example.test";
   await using(var seed=new MySqlCommand("INSERT INTO identity_users(id,user_name,email,password_hash,is_active,is_deleted,created_at)VALUES(@id,@name,@email,@hash,1,0,UTC_TIMESTAMP(6))",c)){seed.Parameters.AddWithValue("@id",legacy.ToString());seed.Parameters.AddWithValue("@name","legacy_"+legacy.ToString("N"));seed.Parameters.AddWithValue("@email",legacyEmail);seed.Parameters.AddWithValue("@hash",hash);await seed.ExecuteNonQueryAsync();}
   Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>repo.CreateAsync(input with{Email=legacyEmail},default))).Status);
   // Ambiguous pre-existing emails cannot authenticate an arbitrary account.
   var duplicate=Guid.NewGuid();ids.Add(duplicate);
   await using(var seed=new MySqlCommand("INSERT INTO identity_users(id,user_name,email,password_hash,is_active,is_deleted,created_at)VALUES(@id,@name,@email,@hash,1,0,UTC_TIMESTAMP(6))",c)){seed.Parameters.AddWithValue("@id",duplicate.ToString());seed.Parameters.AddWithValue("@name","legacy_"+duplicate.ToString("N"));seed.Parameters.AddWithValue("@email",legacyEmail);seed.Parameters.AddWithValue("@hash",hash);await seed.ExecuteNonQueryAsync();}
   Assert.Null(await repo.FindByEmailAsync(legacyEmail,default));
  }
  finally
  {
   foreach(var id in ids){await using var cleanup=new MySqlCommand("DELETE FROM identity_user_profiles WHERE user_id=@id;DELETE FROM identity_signup_emails WHERE user_id=@id;DELETE FROM identity_users WHERE id=@id",c);cleanup.Parameters.AddWithValue("@id",id.ToString());await cleanup.ExecuteNonQueryAsync();}
  }
 }
}
