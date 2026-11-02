using Identity.Application.Features.Profile;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
namespace Identity.Tests;

public class MySqlProfileTests
{
    [IdentityMySqlFact]
    public async Task Existing_accounts_get_profiles_without_changing_email_and_last_write_wins()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION")!;
        var cs = new MySqlConnectionStringBuilder(connectionString);
        Assert.Contains(cs.Server,new[] {"127.0.0.1","localhost","::1"});
        Assert.Equal("zumaris_identity_test",cs.Database);
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync();
        // Only a disposable test database; this is NOT an application users migration.
        await using (var setup = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS identity_users (
                id CHAR(36) COLLATE utf8mb4_unicode_ci NOT NULL PRIMARY KEY,
                user_name VARCHAR(100) NOT NULL, email VARCHAR(255) NOT NULL,
                is_active BOOLEAN NOT NULL, is_deleted BOOLEAN NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,c)) await setup.ExecuteNonQueryAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"Zumaris.sln"))) root=root.Parent;
        Assert.NotNull(root);
        var sql = (await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/003_user_profiles.sql")))
            .Replace("USE zumaris_identity;","USE zumaris_identity_test;");
        for (var i=0;i<2;i++) { await using var migration = new MySqlCommand(sql,c); await migration.ExecuteNonQueryAsync(); }
        var user = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var insert = new MySqlCommand("INSERT INTO identity_users (id,user_name,email,is_active,is_deleted) VALUES (@id,'profile-test','profile@example.test',1,0)",c))
        { insert.Parameters.AddWithValue("@id",user.ToString()); await insert.ExecuteNonQueryAsync(); }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
        var repository = new ProfileAdoNetRepository(config);
        try
        {
            var initial = await repository.GetAsync(user,default);
            Assert.NotNull(initial); Assert.Null(initial.FirstName); Assert.Equal("profile@example.test",initial.Email);
            Assert.Null(await repository.GetAsync(other,default));
            await repository.UpdateAsync(user,new("James","Davidson","+447700900123"),DateTime.UtcNow,default);
            var updated = await repository.UpdateAsync(user,new("Ali","Kamali",null),DateTime.UtcNow,default);
            Assert.Equal("Ali",updated.Profile.FirstName); Assert.Null(updated.Profile.Phone);
            var photo = await repository.SetPhotoAsync(user,"profiles/test.png",DateTime.UtcNow,default);
            Assert.Null(photo.PreviousPhotoPath); Assert.Equal("Ali",photo.Profile.FirstName);
            var removed = await repository.SetPhotoAsync(user,null,DateTime.UtcNow,default);
            Assert.Equal("profiles/test.png",removed.PreviousPhotoPath); Assert.Null(removed.Profile.PhotoPath);
            Assert.Equal("profile@example.test",removed.Profile.Email);
            await Assert.ThrowsAsync<ProfileNotFoundException>(() => repository.UpdateAsync(other,new("Other","User",null),DateTime.UtcNow,default));
            await using var count = new MySqlCommand("SELECT COUNT(*) FROM identity_user_profiles WHERE user_id=@id",c);
            count.Parameters.AddWithValue("@id",user.ToString()); Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
        }
        finally
        {
            await using var cleanup = new MySqlCommand("DELETE FROM identity_user_profiles WHERE user_id=@id; DELETE FROM identity_users WHERE id=@id;",c);
            cleanup.Parameters.AddWithValue("@id",user.ToString()); await cleanup.ExecuteNonQueryAsync();
        }
    }
}
