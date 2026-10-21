using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Property.Application.Features;
using Zumaris.Property.Domain.Entities;
using Zumaris.Property.Infrastructure.Persistence;
using Xunit;

namespace Property.Tests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_PROPERTY_MYSQL_TEST_CONNECTION")))
            Skip="Requires an isolated loopback MySQL 8 schema named zumaris_property_test. See docs/property.md.";
    }
}
[CollectionDefinition("PropertyMySql",DisableParallelization=true)]
public sealed class MySqlCollection : ICollectionFixture<MySqlPropertyFixture>;
public sealed class MySqlPropertyFixture : IAsyncLifetime
{
    public string ConnectionString { get; }=Environment.GetEnvironmentVariable("ZUMARIS_PROPERTY_MYSQL_TEST_CONNECTION")??"";
    public PropertyAdoNetRepository Repository { get; private set; }=null!;
    private string script="";
    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(ConnectionString))return;
        var cs=new MySqlConnectionStringBuilder(ConnectionString);
        if(cs.Server is not ("localhost" or "127.0.0.1" or "::1")||cs.Database!="zumaris_property_test")
            throw new InvalidOperationException("MySQL tests only accept loopback and the disposable zumaris_property_test schema.");
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Run database tests from a source checkout.");
        script=(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/property/001_properties.sql")))
            .Replace("zumaris_property","zumaris_property_test",StringComparison.Ordinal);
        await ApplyMigration();
        Repository=new PropertyAdoNetRepository(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["ConnectionStrings:DefaultConnection"]=ConnectionString }).Build());
    }
    public async Task ApplyMigration()
    {
        await using var c=new MySqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new MySqlCommand(script,c);await cmd.ExecuteNonQueryAsync();
    }
    public async Task<long> Scalar(string sql,Guid id)
    {
        await using var c=new MySqlConnection(ConnectionString);await c.OpenAsync();
        await using var cmd=new MySqlCommand(sql,c);cmd.Parameters.AddWithValue("@id",id.ToString());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    public Task DisposeAsync()=>Task.CompletedTask;
}
[Collection("PropertyMySql")]
public sealed class MySqlTests(MySqlPropertyFixture fixture)
{
    private static PropertyRecord Record(Guid user)=>new(Guid.NewGuid(),"12 Example Road","NW11 8HB","London","GB",51.556m,-0.192m,
        PropertyType.Terraced,3,2,1985,user,DateTime.UtcNow,null,true,1);
    [MySqlFact]
    public async Task Real_migration_create_update_current_and_archive_preserve_history()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var p=Record(user);
        var a=await repo.CreateAsync(p,OwnershipRole.Owner,default);Assert.True(a.IsCurrent);
        Assert.Equal(p.Latitude,a.Property.Latitude);Assert.Equal(p.Bedrooms,a.Property.Bedrooms);
        var b=await repo.CreateAsync(Record(user),OwnershipRole.Tenant,default);Assert.False(b.IsCurrent);
        await repo.SetCurrentAsync(user,b.Property.PropertyId,default);
        var page=await repo.ListAsync(user,1,1,default);Assert.Equal(2,page.Total);Assert.Equal(b.Property.PropertyId,Assert.Single(page.Items).Property.PropertyId);
        var updated=await repo.UpdateAsync(user,p.PropertyId,PropertyFactory.Valid() with { OwnershipRole=OwnershipRole.PropertyManager },1,DateTime.UtcNow,default);
        Assert.Equal(2,updated.Property.Version);Assert.Equal(OwnershipRole.PropertyManager,updated.OwnershipRole);
        await Assert.ThrowsAsync<PropertyConflictException>(()=>repo.UpdateAsync(user,p.PropertyId,PropertyFactory.Valid(),1,DateTime.UtcNow,default));
        await repo.SetCurrentAsync(user,p.PropertyId,default);
        await repo.ArchiveAsync(user,p.PropertyId,2,DateTime.UtcNow,default);
        Assert.Null(await repo.GetAsync(user,p.PropertyId,default));
        Assert.Equal(b.Property.PropertyId,(await repo.GetCurrentAsync(user,default))!.Property.PropertyId);
        Assert.Equal(1,await fixture.Scalar("SELECT COUNT(*) FROM properties WHERE id=@id AND is_active=0 AND version=3",p.PropertyId));
        Assert.Equal(3,await fixture.Scalar("SELECT COUNT(*) FROM property_membership_history WHERE property_id=@id",p.PropertyId));
    }
    [MySqlFact]
    public async Task Real_queries_enforce_membership_and_creator_permissions()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var other=Guid.NewGuid();var a=await repo.CreateAsync(Record(user),OwnershipRole.Owner,default);var id=a.Property.PropertyId;
        Assert.Null(await repo.GetAsync(other,id,default));Assert.Equal(0,(await repo.ListAsync(other,1,20,default)).Total);
        await Assert.ThrowsAsync<PropertyNotFoundException>(()=>repo.SetCurrentAsync(other,id,default));
        await Assert.ThrowsAsync<PropertyNotFoundException>(()=>repo.UpdateAsync(other,id,PropertyFactory.Valid(),1,DateTime.UtcNow,default));
        await Assert.ThrowsAsync<PropertyNotFoundException>(()=>repo.ArchiveAsync(other,id,1,DateTime.UtcNow,default));
        await using var c=new MySqlConnection(fixture.ConnectionString);await c.OpenAsync();
        await using var cmd=new MySqlCommand("INSERT INTO property_memberships(property_id,user_id,ownership_role,is_active,created_at) VALUES(@id,@user,'Owner',1,UTC_TIMESTAMP(6))",c);
        cmd.Parameters.AddWithValue("@id",id.ToString());cmd.Parameters.AddWithValue("@user",other.ToString());await cmd.ExecuteNonQueryAsync();
        Assert.NotNull(await repo.GetAsync(other,id,default));
        await Assert.ThrowsAsync<PropertyForbiddenException>(()=>repo.UpdateAsync(other,id,PropertyFactory.Valid(),1,DateTime.UtcNow,default));
        await Assert.ThrowsAsync<PropertyForbiddenException>(()=>repo.ArchiveAsync(other,id,1,DateTime.UtcNow,default));
    }
    [MySqlFact]
    public async Task Real_concurrent_first_properties_select_exactly_one_current()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>repo.CreateAsync(Record(user),OwnershipRole.Tenant,default)));
        var page=await repo.ListAsync(user,1,20,default);
        Assert.Equal(4,page.Total);Assert.Single(page.Items,x=>x.IsCurrent);Assert.NotNull(await repo.GetCurrentAsync(user,default));
    }
    [MySqlFact]
    public async Task Real_concurrent_updates_have_only_one_winner()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var item=await repo.CreateAsync(Record(user),OwnershipRole.Owner,default);var id=item.Property.PropertyId;
        async Task<bool> Update()
        {
            try { await repo.UpdateAsync(user,id,PropertyFactory.Valid() with { OwnershipRole=OwnershipRole.Tenant },1,DateTime.UtcNow,default);return true; }
            catch(PropertyConflictException){return false;}
        }
        var outcomes=await Task.WhenAll(Update(),Update());Assert.Single(outcomes,x=>x);
        Assert.Equal(2,(await repo.GetAsync(user,id,default))!.Property.Version);
    }
    [MySqlFact]
    public async Task Real_failed_constraint_rolls_back_property_membership_and_preference()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var p=Record(user) with { Latitude=null };
        await Assert.ThrowsAsync<MySqlException>(()=>repo.CreateAsync(p,OwnershipRole.Owner,default));
        Assert.Null(await repo.GetAsync(user,p.PropertyId,default));Assert.Equal(0,(await repo.ListAsync(user,1,20,default)).Total);
        Assert.Equal(0,await fixture.Scalar("SELECT COUNT(*) FROM property_user_preferences WHERE user_id=@id",user));
        var valid=Record(user);
        await Assert.ThrowsAsync<MySqlException>(()=>repo.CreateAsync(valid,(OwnershipRole)999,default));
        Assert.Equal(0,await fixture.Scalar("SELECT COUNT(*) FROM properties WHERE id=@id",valid.PropertyId));
    }
    [MySqlFact]
    public async Task Real_archive_and_selection_cannot_leave_an_archived_property_current()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var first=await repo.CreateAsync(Record(user),OwnershipRole.Owner,default);
        var second=await repo.CreateAsync(Record(user),OwnershipRole.Owner,default);var id=first.Property.PropertyId;
        async Task Select()
        { try { await repo.SetCurrentAsync(user,id,default); } catch(PropertyNotFoundException) { } }
        await Task.WhenAll(Select(),repo.ArchiveAsync(user,id,1,DateTime.UtcNow,default));
        Assert.Equal(second.Property.PropertyId,(await repo.GetCurrentAsync(user,default))!.Property.PropertyId);
        Assert.Single((await repo.ListAsync(user,1,20,default)).Items,x=>x.IsCurrent);
    }
    [MySqlFact]
    public async Task Real_HTTP_CRUD_roundtrip_uses_Identity_session_checks_and_MySQL()
    {
        using var factory=new PropertyFactory { PersistenceOverride=fixture.Repository };
        using var client=factory.Client();
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters={ new JsonStringEnumConverter() } };
        using var created=await client.PostAsJsonAsync("/api/properties",PropertyFactory.Valid(),json);
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var property=(await created.Content.ReadFromJsonAsync<PropertyDto>(json))!;
        Assert.True(property.IsCurrent);
        var list=(await client.GetFromJsonAsync<PropertyPage>("/api/properties",json))!;
        Assert.Equal(property.PropertyId,Assert.Single(list.Items).PropertyId);
        using var other=factory.Client(userId:Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/properties/{property.PropertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync($"/api/properties/{property.PropertyId}?version=1")).StatusCode);
        Assert.True(factory.Validator.Calls>=4);
    }
    [MySqlFact]
    public async Task Real_migration_can_be_repeated_without_replacing_existing_records()
    {
        var repo=fixture.Repository;var user=Guid.NewGuid();var a=await repo.CreateAsync(Record(user),OwnershipRole.Owner,default);
        await fixture.ApplyMigration();Assert.NotNull(await repo.GetAsync(user,a.Property.PropertyId,default));
    }
}
