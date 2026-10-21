using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Property.Application.Abstractions;
using Zumaris.Property.Application.Features;
using Zumaris.Property.Domain.Entities;

namespace Zumaris.Property.Infrastructure.Persistence;

public sealed class PropertyAdoNetRepository(IConfiguration configuration) : IPropertyRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required.");
    private const string From = """
        FROM properties p
        JOIN property_memberships m ON m.property_id=p.id AND m.user_id=@user AND m.is_active=1
        LEFT JOIN property_user_preferences pref ON pref.user_id=@user
        WHERE p.is_active=1
        """;
    private const string Select = "SELECT p.*,m.ownership_role,COALESCE(pref.current_property_id=p.id,0) AS is_current ";
    private static MySqlCommand Command(MySqlConnection c, MySqlTransaction? tx, string sql, params (string,object?)[] values)
    {
        var cmd=new MySqlCommand(sql,c,tx);
        foreach (var (key,value) in values) cmd.Parameters.AddWithValue(key,value ?? DBNull.Value);
        return cmd;
    }
    private static string? Text(MySqlDataReader r,string field) => r.IsDBNull(r.GetOrdinal(field)) ? null : r.GetString(field);
    private static DateTime? Date(MySqlDataReader r,string field) => r.IsDBNull(r.GetOrdinal(field)) ? null : DateTime.SpecifyKind(r.GetDateTime(field),DateTimeKind.Utc);
    private static PropertyAccess Read(MySqlDataReader r) => new(new PropertyRecord(r.GetGuid(r.GetOrdinal("id")),r.GetString("address_line"),
        Text(r,"postcode"),r.GetString("city"),r.GetString("country_code"),
        r.IsDBNull(r.GetOrdinal("latitude")) ? null : r.GetDecimal("latitude"),
        r.IsDBNull(r.GetOrdinal("longitude")) ? null : r.GetDecimal("longitude"),Enum.Parse<PropertyType>(r.GetString("property_type")),
        r.GetInt32("bedrooms"),r.GetInt32("bathrooms"),r.IsDBNull(r.GetOrdinal("year_built")) ? null : r.GetInt32("year_built"),
        r.GetGuid(r.GetOrdinal("created_by_user_id")),Date(r,"created_at")!.Value,Date(r,"updated_at"),r.GetBoolean("is_active"),r.GetInt32("version")),
        Enum.Parse<OwnershipRole>(r.GetString("ownership_role")),r.GetBoolean("is_current"));
    private static async Task<PropertyAccess?> Access(MySqlConnection c,MySqlTransaction? tx,Guid user,Guid id,CancellationToken ct,bool locked=false)
    {
        await using var cmd=Command(c,tx,Select+From+" AND p.id=@id"+(locked ? " FOR UPDATE" : ""),("@user",user.ToString()),("@id",id.ToString()));
        await using var reader=await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow,ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    private static async Task LockPreferences(MySqlConnection c,MySqlTransaction tx,Guid user,CancellationToken ct)
    {
        // Every user mutation takes the same preference row first, serializing first-property/current selection.
        await using var cmd=Command(c,tx,"INSERT INTO property_user_preferences (user_id,current_property_id) VALUES (@user,NULL) ON DUPLICATE KEY UPDATE user_id=@user",("@user",user.ToString()));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task SelectCurrent(MySqlConnection c,MySqlTransaction tx,Guid user,Guid? id,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"UPDATE property_user_preferences SET current_property_id=@id WHERE user_id=@user",
            ("@id",id?.ToString()),("@user",user.ToString()));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task Audit(MySqlConnection c,MySqlTransaction tx,Guid property,Guid user,OwnershipRole? oldRole,OwnershipRole? newRole,string kind,DateTime now,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"""
            INSERT INTO property_membership_history (id,property_id,user_id,actor_user_id,from_role,to_role,event_type,occurred_at)
            VALUES (@id,@property,@user,@user,@old,@new,@kind,@now)
            """,("@id",Guid.NewGuid().ToString()),("@property",property.ToString()),("@user",user.ToString()),
            ("@old",oldRole?.ToString()),("@new",newRole?.ToString()),("@kind",kind),("@now",now));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static void CanModify(PropertyAccess a,Guid user,int version)
    {
        if (a.Property.CreatedByUserId!=user) throw new PropertyForbiddenException();
        if (a.Property.Version!=version) throw new PropertyConflictException();
    }
    private static async Task<T> Mutation<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (MySqlException ex) when (ex.Number is 1213 or 1205) { throw new PropertyConflictException(); }
    }
    public Task<PropertyAccess> CreateAsync(PropertyRecord p,OwnershipRole role,CancellationToken ct) => Mutation(async () =>
    {
        await using var c=new MySqlConnection(connectionString); await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        await LockPreferences(c,tx,p.CreatedByUserId,ct);
        await using var cmd=Command(c,tx,"""
            INSERT INTO properties (id,address_line,postcode,city,country_code,latitude,longitude,property_type,bedrooms,bathrooms,year_built,
                created_by_user_id,created_at,updated_at,is_active,version)
            VALUES (@id,@address,@postcode,@city,@country,@lat,@lon,@type,@beds,@baths,@year,@user,@now,NULL,1,1)
            """,("@id",p.PropertyId.ToString()),("@address",p.AddressLine),("@postcode",p.Postcode),("@city",p.City),("@country",p.CountryCode),
            ("@lat",p.Latitude),("@lon",p.Longitude),("@type",p.PropertyType.ToString()),("@beds",p.Bedrooms),("@baths",p.Bathrooms),
            ("@year",p.YearBuilt),("@user",p.CreatedByUserId.ToString()),("@now",p.CreatedAtUtc));
        await cmd.ExecuteNonQueryAsync(ct);
        await using var member=Command(c,tx,"""
            INSERT INTO property_memberships (property_id,user_id,ownership_role,is_active,created_at,updated_at)
            VALUES (@id,@user,@role,1,@now,NULL)
            """,("@id",p.PropertyId.ToString()),("@user",p.CreatedByUserId.ToString()),("@role",role.ToString()),("@now",p.CreatedAtUtc));
        await member.ExecuteNonQueryAsync(ct);
        await Audit(c,tx,p.PropertyId,p.CreatedByUserId,null,role,"Created",p.CreatedAtUtc,ct);
        await using var current=Command(c,tx,"SELECT p.id "+From+" AND pref.current_property_id=p.id LIMIT 1",("@user",p.CreatedByUserId.ToString()));
        if (await current.ExecuteScalarAsync(ct) is null) await SelectCurrent(c,tx,p.CreatedByUserId,p.PropertyId,ct);
        var result=(await Access(c,tx,p.CreatedByUserId,p.PropertyId,ct))!;
        await tx.CommitAsync(ct);return result;
    });
    public async Task<PropertyAccess?> GetAsync(Guid userId,Guid propertyId,CancellationToken ct)
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        return await Access(c,null,userId,propertyId,ct);
    }
    public async Task<PropertyAccess?> GetCurrentAsync(Guid userId,CancellationToken ct)
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        await using var cmd=Command(c,null,Select+From+" AND pref.current_property_id=p.id",("@user",userId.ToString()));
        await using var reader=await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow,ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<(IReadOnlyList<PropertyAccess> Items,long Total)> ListAsync(Guid userId,int pageNumber,int pageSize,CancellationToken ct)
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using var count=Command(c,tx,"SELECT COUNT(*) "+From,("@user",userId.ToString()));
        var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd=Command(c,tx,Select+From+" ORDER BY is_current DESC,p.created_at DESC,p.id DESC LIMIT @size OFFSET @offset",
            ("@user",userId.ToString()),("@size",pageSize),("@offset",((long)pageNumber-1)*pageSize));
        var items=new List<PropertyAccess>();
        await using (var reader=await cmd.ExecuteReaderAsync(ct)) while(await reader.ReadAsync(ct)) items.Add(Read(reader));
        await tx.CommitAsync(ct);return (items,total);
    }
    public Task<PropertyAccess> UpdateAsync(Guid userId,Guid propertyId,CreateProperty p,int version,DateTime now,CancellationToken ct) => Mutation(async () =>
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await LockPreferences(c,tx,userId,ct);
        var before=await Access(c,tx,userId,propertyId,ct,true) ?? throw new PropertyNotFoundException();CanModify(before,userId,version);
        await using var cmd=Command(c,tx,"""
            UPDATE properties SET address_line=@address,postcode=@postcode,city=@city,country_code=@country,latitude=@lat,longitude=@lon,
                property_type=@type,bedrooms=@beds,bathrooms=@baths,year_built=@year,updated_at=@now,version=version+1
            WHERE id=@id AND version=@version AND is_active=1
            """,("@address",p.AddressLine),("@postcode",p.Postcode),("@city",p.City),("@country",p.CountryCode),("@lat",p.Latitude),("@lon",p.Longitude),
            ("@type",p.PropertyType.ToString()),("@beds",p.Bedrooms),("@baths",p.Bathrooms),("@year",p.YearBuilt),("@now",now),
            ("@id",propertyId.ToString()),("@version",version));
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new PropertyConflictException();
        await using var member=Command(c,tx,"UPDATE property_memberships SET ownership_role=@role,updated_at=@now WHERE property_id=@id AND user_id=@user AND is_active=1",
            ("@role",p.OwnershipRole.ToString()),("@now",now),("@id",propertyId.ToString()),("@user",userId.ToString()));
        await member.ExecuteNonQueryAsync(ct);
        if(before.OwnershipRole!=p.OwnershipRole) await Audit(c,tx,propertyId,userId,before.OwnershipRole,p.OwnershipRole,"RoleChanged",now,ct);
        var result=(await Access(c,tx,userId,propertyId,ct))!;await tx.CommitAsync(ct);return result;
    });
    public Task<PropertyAccess> SetCurrentAsync(Guid userId,Guid propertyId,CancellationToken ct) => Mutation(async () =>
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await LockPreferences(c,tx,userId,ct);
        var item=await Access(c,tx,userId,propertyId,ct,true) ?? throw new PropertyNotFoundException();
        await SelectCurrent(c,tx,userId,propertyId,ct);await tx.CommitAsync(ct);return item with { IsCurrent=true };
    });
    public Task ArchiveAsync(Guid userId,Guid propertyId,int version,DateTime now,CancellationToken ct) => Mutation(async () =>
    {
        await using var c=new MySqlConnection(connectionString);await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await LockPreferences(c,tx,userId,ct);
        var item=await Access(c,tx,userId,propertyId,ct,true) ?? throw new PropertyNotFoundException();CanModify(item,userId,version);
        await using var archive=Command(c,tx,"UPDATE properties SET is_active=0,updated_at=@now,version=version+1 WHERE id=@id AND version=@version AND is_active=1",
            ("@id",propertyId.ToString()),("@version",version),("@now",now));
        if(await archive.ExecuteNonQueryAsync(ct)!=1)throw new PropertyConflictException();
        // Retain relationship and declaration history; no property or order is physically deleted.
        await using var history=Command(c,tx,"""
            INSERT INTO property_membership_history (id,property_id,user_id,actor_user_id,from_role,to_role,event_type,occurred_at)
            SELECT UUID(),property_id,user_id,@actor,ownership_role,NULL,'PropertyArchived',@now
            FROM property_memberships WHERE property_id=@id AND is_active=1
            """,("@actor",userId.ToString()),("@now",now),("@id",propertyId.ToString()));
        await history.ExecuteNonQueryAsync(ct);
        await using var deactivate=Command(c,tx,"UPDATE property_memberships SET is_active=0,updated_at=@now WHERE property_id=@id",
            ("@now",now),("@id",propertyId.ToString()));await deactivate.ExecuteNonQueryAsync(ct);
        await using var clear=Command(c,tx,"UPDATE property_user_preferences SET current_property_id=NULL WHERE current_property_id=@id",("@id",propertyId.ToString()));
        await clear.ExecuteNonQueryAsync(ct);
        if(item.IsCurrent)
        {
            await using var next=Command(c,tx,"SELECT p.id "+From+" ORDER BY p.created_at DESC,p.id DESC LIMIT 1",("@user",userId.ToString()));
            var value=await next.ExecuteScalarAsync(ct);
            await SelectCurrent(c,tx,userId,value switch { Guid id => id, string id => Guid.Parse(id), _ => (Guid?)null },ct);
        }
        await tx.CommitAsync(ct);return 0;
    });
}
