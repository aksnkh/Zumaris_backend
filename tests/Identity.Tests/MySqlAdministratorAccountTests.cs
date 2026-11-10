using Identity.Application.Abstractions.Persistence;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Signup;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
namespace Identity.Tests;
public sealed class AdministratorMySqlFactAttribute:FactAttribute
{
    public AdministratorMySqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_ADMIN_MYSQL_TEST_CONNECTION")))
            Skip="Requires isolated loopback zumaris_identity_admin_test database.";
    }
}
public class MySqlAdministratorAccountTests
{
    [AdministratorMySqlFact]
    public async Task Creation_is_atomic_duplicate_safe_audited_and_bootstrap_is_one_time_even_under_concurrency()
    {
        var cs=Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_ADMIN_MYSQL_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs);
        Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_identity_admin_test",parsed.Database);
        await using var db=new MySqlConnection(cs);await db.OpenAsync();
        await using(var setup=new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS identity_users (
             id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
             user_name VARCHAR(100) NOT NULL UNIQUE,email VARCHAR(254) NOT NULL,
             password_hash VARCHAR(1024) NULL,is_active BOOLEAN NOT NULL,is_deleted BOOLEAN NOT NULL,created_at DATETIME(6) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,db))await setup.ExecuteNonQueryAsync();
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
        var migrations=new List<string>();
        foreach(var file in new[]{"003_user_profiles.sql","004_platform_administrators.sql","005_signup.sql","007_administrator_accounts.sql"})
        {
            var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity",file))).Replace("zumaris_identity","zumaris_identity_admin_test");migrations.Add(sql);
            for(int i=0;i<2;i++){await using var command=new MySqlCommand(sql,db);await command.ExecuteNonQueryAsync();}
        }
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
        var accounts=new AdministratorAccountRepository(config);var users=new SignupRepository(config);var members=new PlatformAdminRepository(config);
        var hasher=new PasswordHasher();var password="Password-for-SQL-admin-1";var hash=hasher.Hash(password);var now=DateTime.UtcNow;
        var run=Guid.NewGuid().ToString("N");var a=new SignupAccount("First","Admin","first-"+run+"@example.test","+447700900123",hash);var b=a with{Email="second-"+run+"@example.test"};
        async Task Run(string sql,params(string,object)[] args){await using var cmd=new MySqlCommand(sql,db);foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v);await cmd.ExecuteNonQueryAsync();}
        async Task<long> Count(string table,string email){await using var c=new MySqlCommand("SELECT COUNT(*) FROM "+table+" WHERE "+(table=="identity_signup_emails"?"normalized_email":"email")+"=@email",db);c.Parameters.AddWithValue("@email",email);return Convert.ToInt64(await c.ExecuteScalarAsync());}
        async Task<AdministratorAccountResponse?> Bootstrap(SignupAccount input)
        {try{return await accounts.CreateAsync(input,null,now,default);}catch(SignupException ex){Assert.Equal(409,ex.Status);return null;}}
        try
        {
            // A failed profile INSERT must not consume the setup marker or leave a reserved email/user.
            var invalid=a with{Email="invalid-"+run+"@example.test",Phone="invalid"};
            await Assert.ThrowsAsync<MySqlException>(()=>accounts.CreateAsync(invalid,null,now,default));Assert.Equal(0,await Count("identity_users",invalid.Email));Assert.Equal(0,await Count("identity_signup_emails",invalid.Email));
            var raced=await Task.WhenAll(Bootstrap(a),Bootstrap(b));var first=Assert.Single(raced,x=>x is not null)!;
            Assert.True(await members.IsAdminAsync(first.UserId,default));Assert.Equal("Admin",first.Role);Assert.Null(first.CreatedByUserId);
            var stored=await users.FindByEmailAsync(first.Email.ToUpperInvariant(),default);Assert.NotNull(stored);Assert.True(hasher.Verify(password,stored.PasswordHash!));Assert.NotEqual(password,stored.PasswordHash);
            var other=a with{Email="other-"+run+"@example.test"};
            var second=await accounts.CreateAsync(other,first.UserId,now,default);Assert.Equal(first.UserId,second.CreatedByUserId);Assert.True(await members.IsAdminAsync(second.UserId,default));
            Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(other,first.UserId,now,default))).Status);
            // Sharing an email with a regular customer never silently upgrades that account.
            var customerInput=a with{Email="customer-"+run+"@example.test"};var customer=await users.CreateAsync(customerInput,default);
            Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(customerInput,first.UserId,now,default))).Status);Assert.False(await members.IsAdminAsync(customer.Id,default));
            Assert.Equal(403,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(a with{Email="forbidden-"+run+"@example.test"},customer.Id,now,default))).Status);
            // Fail after inserting the role: user, profile, reservation and role must all roll back.
            await Run("CREATE TRIGGER fail_administrator_audit BEFORE INSERT ON identity_administrator_account_creations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test audit failure'");
            var rollback=a with{Email="rollback-"+run+"@example.test"};
            try{await Assert.ThrowsAsync<MySqlException>(()=>accounts.CreateAsync(rollback,first.UserId,now,default));}
            finally{await Run("DROP TRIGGER fail_administrator_audit");}
            Assert.Equal(0,await Count("identity_users",rollback.Email));Assert.Equal(0,await Count("identity_signup_emails",rollback.Email));
            // Reapplying the migration and revoking every membership never reopen bootstrap.
            await Run(migrations[^1]);await Run("UPDATE platform_administrators SET is_active=0 WHERE user_id IN (@one,@two)",("@one",first.UserId.ToString()),("@two",second.UserId.ToString()));
            Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(a with{Email="late-"+run+"@example.test"},null,now,default))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(a with{Email="revoked-"+run+"@example.test"},first.UserId,now,default))).Status);
            await using(var audit=new MySqlCommand("SELECT source,created_by_user_id FROM identity_administrator_account_creations ORDER BY created_at,user_id",db))
            await using(var reader=await audit.ExecuteReaderAsync())
            {var sources=new List<string>();while(await reader.ReadAsync()){sources.Add(reader.GetString(0));if(reader.GetString(0)=="Bootstrap")Assert.True(reader.IsDBNull(1));else Assert.Equal(first.UserId,reader.GetGuid(1));}Assert.Equal(2,sources.Count);Assert.Contains("Bootstrap",sources);Assert.Contains("Administrator",sources);}
            await Run("DELETE FROM identity_administrator_setup WHERE id=1");await Run(migrations[^1]);
            await Run("DELETE FROM platform_administrators WHERE user_id IN (@one,@two)",("@one",first.UserId.ToString()),("@two",second.UserId.ToString()));
            Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync(a with{Email="deleted-"+run+"@example.test"},null,now,default))).Status);
        }
        finally
        {
            // This disposable schema is dedicated to this fact. Restore only its test fixtures.
            await Run("DROP TRIGGER IF EXISTS fail_administrator_audit;DELETE FROM identity_administrator_account_creations;DELETE FROM platform_administrators;DELETE FROM identity_user_profiles;DELETE FROM identity_signup_emails;DELETE FROM identity_users;DELETE FROM identity_administrator_setup WHERE id=1;INSERT INTO identity_administrator_setup(id)VALUES(1);");
        }
    }
}
