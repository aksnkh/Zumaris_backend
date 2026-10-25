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
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        // Use the actual manual migration, exclusively in the isolated test database.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"Zumaris.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/001_auth_sessions.sql"));
        await using (var command = new MySqlCommand(sql, connection)) await command.ExecuteNonQueryAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
        var repository = new SessionAdoNetRepository(config);
        var now = DateTime.UtcNow;
        var session = new AuthSession(Guid.NewGuid(),Guid.NewGuid(),"mysql-test",now,now.AddHours(1),null);
        var oldHash = Guid.NewGuid().ToString("N").PadRight(64,'a');
        var newHash = Guid.NewGuid().ToString("N").PadRight(64,'b');
        try
        {
            await repository.CreateAsync(session,oldHash);
            var read = await repository.FindByTokenAsync(oldHash);
            Assert.NotNull(read);
            Assert.Equal(session.Id,read.Id);
            Assert.Equal(session.UserId,read.UserId);
            Assert.Equal(session.Id,Assert.Single(await repository.ListAsync(session.UserId)).Id);
            Assert.True(await repository.RotateAsync(session.Id,oldHash,newHash));
            Assert.Equal(session.Id,(await repository.FindByTokenAsync(newHash))!.Id);
            Assert.False(await repository.RotateAsync(session.Id,oldHash,newHash));
            Assert.False(await repository.IsActiveAsync(session.Id,session.UserId));
        }
        finally
        {
            await using var cleanup = new MySqlCommand("DELETE FROM zumaris_auth_sessions WHERE id=@id",connection);
            cleanup.Parameters.AddWithValue("@id",session.Id.ToString());
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
