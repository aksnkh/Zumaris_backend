using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityMySqlFactAttribute : FactAttribute
{
    public IdentityMySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION")))
            Skip = "Requires an isolated loopback database named zumaris_identity_test.";
    }
}

public class MySqlSessionTests
{
    [IdentityMySqlFact]
    public async Task Guid_sessions_can_be_read_rotated_listed_and_revoked_with_mysql()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION")!;
        var cs = new MySqlConnectionStringBuilder(connectionString);
        Assert.Contains(cs.Server, new[] { "127.0.0.1", "localhost", "::1" });
        Assert.Equal("zumaris_identity_test", cs.Database);
        await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder(connectionString){AllowUserVariables=true}.ConnectionString);
        await connection.OpenAsync();
        // Use the actual manual migration, exclusively in the isolated test database.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"Zumaris.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/001_auth_sessions.sql"));
        await using (var command = new MySqlCommand(sql, connection)) await command.ExecuteNonQueryAsync();
        var appSql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/006_application_sessions.sql"))).Replace("zumaris_identity","zumaris_identity_test");
        for(int i=0;i<2;i++){await using var migration=new MySqlCommand(appSql,connection);await migration.ExecuteNonQueryAsync();}
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
        var repository = new SessionAdoNetRepository(config);
        var now = DateTime.UtcNow;
        var session = new AuthSession(Guid.NewGuid(),Guid.NewGuid(),"mysql-test",now,now.AddHours(1),null){Application="App"};
        var techSession=session with{Id=Guid.NewGuid(),Application="Tech",ProviderId=Guid.NewGuid()};
        var legacyId=Guid.NewGuid();
        var oldHash = Guid.NewGuid().ToString("N").PadRight(64,'a');
        var newHash = Guid.NewGuid().ToString("N").PadRight(64,'b');
        try
        {
            await repository.CreateAsync(session,oldHash);
            await repository.CreateAsync(techSession,Guid.NewGuid().ToString("N").PadRight(64,'d'));
            await using(var legacy=new MySqlCommand("INSERT INTO zumaris_auth_sessions(id,user_id,user_name,created_at,expires_at)VALUES(@id,@user,'legacy',UTC_TIMESTAMP(6),DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 1 DAY))",connection))
            {legacy.Parameters.AddWithValue("@id",legacyId.ToString());legacy.Parameters.AddWithValue("@user",session.UserId.ToString());await legacy.ExecuteNonQueryAsync();}
            await using(var repeat=new MySqlCommand(appSql,connection))await repeat.ExecuteNonQueryAsync();
            Assert.False(await repository.IsActiveAsync(legacyId,session.UserId));
            var storedTech=await repository.FindByIdAsync(techSession.Id,session.UserId);Assert.NotNull(storedTech);Assert.Equal("Tech",storedTech.Application);Assert.Equal(techSession.ProviderId,storedTech.ProviderId);

            var read = await repository.FindByTokenAsync(oldHash);
            Assert.NotNull(read);
            Assert.Equal(session.Id,read.Id);
            Assert.Equal(session.UserId,read.UserId);
            Assert.Equal("App",read.Application);
            Assert.Null(read.ProviderId);
            Assert.Equal(2,(await repository.ListAsync(session.UserId)).Count);
            Assert.True(await repository.RotateAsync(session.Id,oldHash,newHash));
            Assert.Equal(session.Id,(await repository.FindByTokenAsync(newHash))!.Id);
            Assert.False(await repository.RotateAsync(session.Id,oldHash,newHash));
            Assert.False(await repository.IsActiveAsync(session.Id,session.UserId));
        }
        finally
        {
            await using var cleanup = new MySqlCommand("DELETE FROM zumaris_auth_sessions WHERE user_id=@user",connection);
            cleanup.Parameters.AddWithValue("@user",session.UserId.ToString());
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
