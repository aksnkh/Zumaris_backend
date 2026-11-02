using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class QuoteAdoNetRepository(IConfiguration configuration) : IQuoteRepository
{
    private readonly string cs=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection is required.");
    private const string Effective="CASE WHEN q.status='Pending' AND (r.status<>'Submitted' OR r.version<>q.request_version) THEN 'Withdrawn' WHEN q.status='Pending' AND q.expires_at IS NOT NULL AND q.expires_at<=@now THEN 'Expired' ELSE q.status END";
    private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params(string,object?)[] args)
    {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);return cmd;}
    private static string? Text(MySqlDataReader r,string n)=>r.IsDBNull(r.GetOrdinal(n))?null:r.GetString(n);
    private static DateTime? Date(MySqlDataReader r,string n)=>r.IsDBNull(r.GetOrdinal(n))?null:DateTime.SpecifyKind(r.GetDateTime(n),DateTimeKind.Utc);
    private static QuoteContext Context(MySqlDataReader r)=>new(r.GetGuid(r.GetOrdinal("id")),r.GetGuid(r.GetOrdinal("user_id")),r.GetGuid(r.GetOrdinal("property_id")),
        r.GetInt32("category_id"),r.GetInt32("area_type_id"),r.GetString("request_number"),r.GetString("description"),r.GetString("status"),r.GetInt32("version"),
        Text(r,"category_name"),Text(r,"service_area"),Text(r,"property_address"),r.IsDBNull(r.GetOrdinal("preferred_date"))?null:DateOnly.FromDateTime(r.GetDateTime("preferred_date")),
        r.IsDBNull(r.GetOrdinal("preferred_start_time"))?null:TimeOnly.FromTimeSpan(r.GetTimeSpan("preferred_start_time")),
        r.IsDBNull(r.GetOrdinal("preferred_end_time"))?null:TimeOnly.FromTimeSpan(r.GetTimeSpan("preferred_end_time")),r.GetString("time_zone_id"));
    private static QuoteDto Read(MySqlDataReader r)=>JsonSerializer.Deserialize<QuoteDto>(r.GetString("snapshot"))! with {
        Status=r.GetString("effective_status"),DecidedAtUtc=Date(r,"decided_at"),DecisionReason=Text(r,"decision_reason")
    };
    private static async Task<QuoteContext?> ReadContext(MySqlConnection c,MySqlTransaction? tx,Guid id,bool locked,CancellationToken ct)
    {
        await using var cmd=Cmd(c,tx,"SELECT * FROM service_requests WHERE id=@id"+(locked?" FOR UPDATE":""),("@id",id.ToString()));
        await using var r=await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow,ct);return await r.ReadAsync(ct)?Context(r):null;
    }
    public async Task<QuoteContext?> ContextAsync(Guid requestId,CancellationToken ct)
    {await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);return await ReadContext(c,null,requestId,false,ct);}
    private static async Task<QuoteDto> ReadOne(MySqlConnection c,MySqlTransaction? tx,Guid id,DateTime now,CancellationToken ct)
    {
        await using var cmd=Cmd(c,tx,"SELECT q.*,"+Effective+" AS effective_status FROM service_request_quotes q JOIN service_requests r ON r.id=q.request_id WHERE q.id=@id",("@id",id.ToString()),("@now",now));
        await using var r=await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow,ct);if(!await r.ReadAsync(ct))throw new QuoteNotFoundException();return Read(r);
    }
    public async Task<QuoteDto> CreateAsync(Guid actor,Guid requestId,CreateQuoteRequest input,ProviderQuoteIdentity provider,QuoteDisplay display,int expectedRequestVersion,string hash,DateTime now,CancellationToken ct)
    {
        await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        var request=await ReadContext(c,tx,requestId,true,ct)??throw new QuoteNotFoundException();
        if(request.UserId==actor||provider.UserId!=actor||!provider.IsVerified||provider.CategoryId!=request.CategoryId)throw new QuoteForbiddenException();
        if(request.Status!="Submitted")throw new QuoteConflictException("Only Submitted requests can receive quotes.");
        if(request.RequestVersion!=expectedRequestVersion)throw new QuoteConflictException("Request changed while preparing the quote. Reload before trying again.");
        // Release expired/stale pending slots before allowing a replacement.
        await using(var stale=Cmd(c,tx,"""
            UPDATE service_request_quotes SET status=CASE WHEN request_version<>@version THEN 'Withdrawn' ELSE 'Expired' END,
            decided_at=@now,decision_reason=CASE WHEN request_version<>@version THEN 'Request changed.' ELSE 'Quote expired.' END
            WHERE request_id=@request AND status='Pending' AND (request_version<>@version OR (expires_at IS NOT NULL AND expires_at<=@now))
            """,("@request",requestId.ToString()),("@version",request.RequestVersion),("@now",now)))await stale.ExecuteNonQueryAsync(ct);
        Guid? duplicate=null;string? previousHash=null;
        await using(var existing=Cmd(c,tx,"SELECT id,request_hash FROM service_request_quotes WHERE request_id=@request AND provider_id=@provider AND status='Pending'",("@request",requestId.ToString()),("@provider",provider.ProviderId.ToString())))
        {await using var r=await existing.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){duplicate=r.GetGuid(0);previousHash=r.GetString(1);}}
        if(duplicate.HasValue)
        {
            if(previousHash!=hash)throw new QuoteConflictException("Withdraw the pending quote before submitting a different price.");
            var repeated=await ReadOne(c,tx,duplicate.Value,now,ct);await tx.CommitAsync(ct);return repeated with {PropertyAddress=null};
        }
        var id=Guid.NewGuid();
        var quote=new QuoteDto(id,requestId,request.RequestNumber,request.PropertyId,request.PropertyAddress,request.CategoryId,request.AreaTypeId,
            display.CategoryName??request.CategoryName,display.AreaName??request.AreaName,display.CategoryIconUrl,request.Description,
            request.PreferredDate,request.PreferredStartTime,request.PreferredEndTime,request.TimeZoneId,provider.ProviderId,provider.DisplayName,provider.PhotoUrl,
            provider.Rating,provider.CompletedJobs,provider.IsVerified,"Pending",input.LabourAmount,input.CalloutAmount,input.LabourAmount+input.CalloutAmount,
            input.CurrencyCode,input.ProviderNote,input.PriceSummary!,now,input.ExpiresAtUtc,null,null);
        await using(var insert=Cmd(c,tx,"""
            INSERT INTO service_request_quotes
            (id,request_id,customer_id,property_id,provider_id,provider_user_id,request_version,status,labour_amount,callout_amount,total_amount,currency_code,
             request_hash,snapshot,created_at,expires_at)
            VALUES (@id,@request,@customer,@property,@provider,@actor,@version,'Pending',@labour,@callout,@total,@currency,@hash,@snapshot,@now,@expiry)
            """,("@id",id.ToString()),("@request",requestId.ToString()),("@customer",request.UserId.ToString()),("@property",request.PropertyId.ToString()),
            ("@provider",provider.ProviderId.ToString()),("@actor",actor.ToString()),("@version",request.RequestVersion),("@labour",input.LabourAmount),
            ("@callout",input.CalloutAmount),("@total",quote.TotalAmount),("@currency",quote.CurrencyCode),("@hash",hash),("@snapshot",JsonSerializer.Serialize(quote)),("@now",now),("@expiry",input.ExpiresAtUtc)))
            await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return quote with {PropertyAddress=null};
    }
    public async Task<QuotePage> ListAsync(Guid actor,bool sent,string? status,Guid? propertyId,int page,int size,DateTime now,CancellationToken ct)
    {
        var where=(sent?"q.provider_user_id=@actor":"q.customer_id=@actor AND r.user_id=@actor")+
            (status is null?"":" AND ("+Effective+")=@status")+(propertyId.HasValue?" AND q.property_id=@property":"");
        await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        MySqlCommand Query(string sql)=>Cmd(c,tx,sql,("@actor",actor.ToString()),("@now",now),("@status",status),("@property",propertyId?.ToString()),("@size",size),("@offset",((long)page-1)*size));
        await using var count=Query("SELECT COUNT(*) FROM service_request_quotes q JOIN service_requests r ON r.id=q.request_id WHERE "+where);
        var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd=Query("SELECT q.*,"+Effective+" AS effective_status FROM service_request_quotes q JOIN service_requests r ON r.id=q.request_id WHERE "+where+" ORDER BY q.created_at DESC,q.id DESC LIMIT @size OFFSET @offset");
        var items=new List<QuoteDto>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct)){var item=Read(r);items.Add(sent&&item.Status!="Accepted"?item with {PropertyAddress=null}:item);}
        await tx.CommitAsync(ct);return new(items,page,size,total);
    }
    public async Task<QuoteDto?> GetAsync(Guid actor,Guid id,DateTime now,CancellationToken ct)
    {
        await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);
        await using var cmd=Cmd(c,null,"SELECT q.*,"+Effective+" AS effective_status FROM service_request_quotes q JOIN service_requests r ON r.id=q.request_id WHERE q.id=@id AND ((q.customer_id=@actor AND r.user_id=@actor) OR q.provider_user_id=@actor)",("@id",id.ToString()),("@actor",actor.ToString()),("@now",now));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        var item=Read(r);return r.GetGuid(r.GetOrdinal("customer_id"))!=actor&&item.Status!="Accepted"?item with {PropertyAddress=null}:item;
    }
    public async Task<QuoteDto> DecideAsync(Guid actor,Guid quoteId,string decision,DateTime now,CancellationToken ct)
    {
        if(decision is not("Accepted" or "Declined" or "Withdrawn"))throw new ArgumentException("Invalid decision.");
        await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        Guid requestId;
        await using(var find=Cmd(c,tx,"SELECT request_id FROM service_request_quotes WHERE id=@id AND "+(decision=="Withdrawn"?"provider_user_id":"customer_id")+"=@actor",("@id",quoteId.ToString()),("@actor",actor.ToString())))
        {var value=await find.ExecuteScalarAsync(ct);if(value is null)throw new QuoteNotFoundException();requestId=value is Guid guid?guid:Guid.Parse((string)value);}
        var request=await ReadContext(c,tx,requestId,true,ct)??throw new QuoteNotFoundException();
        var quote=await ReadOne(c,tx,quoteId,now,ct);
        if(decision!="Withdrawn"&&request.UserId!=actor)throw new QuoteNotFoundException();
        if(quote.Status==decision){await tx.CommitAsync(ct);return decision=="Withdrawn"?quote with {PropertyAddress=null}:quote;}
        if(quote.Status!="Pending")throw new QuoteConflictException("Only a pending, current and unexpired quote can be changed.");
        if(request.Status!="Submitted")throw new QuoteConflictException("The request is no longer available for this decision.");
        // ReadOne calculates Withdrawn when the request version changed; no client version is required.
        await using(var update=Cmd(c,tx,"UPDATE service_request_quotes SET status=@status,decided_at=@now,decision_reason=@reason WHERE id=@id AND status='Pending'",("@status",decision),("@now",now),("@reason",decision=="Accepted"?"Customer accepted.":decision=="Declined"?"Customer declined.":"Provider withdrew."),("@id",quoteId.ToString())))
            if(await update.ExecuteNonQueryAsync(ct)!=1)throw new QuoteConflictException("Quote status changed. Reload before trying again.");
        if(decision=="Accepted")
        {
            await using(var assign=Cmd(c,tx,"""
                UPDATE service_requests SET status='Assigned',technician_id=@provider,technician_name=@name,updated_at=@now,version=version+1
                WHERE id=@id AND status='Submitted'
                """,("@id",requestId.ToString()),("@provider",quote.ProviderId.ToString()),("@name",quote.ProviderName),("@now",now)))
                if(await assign.ExecuteNonQueryAsync(ct)!=1)throw new QuoteConflictException("Request is already assigned.");
            await using(var history=Cmd(c,tx,"""
                INSERT INTO service_request_status_history (id,request_id,from_status,to_status,actor_user_id,occurred_at,reason)
                VALUES (@id,@request,'Submitted','Assigned',@actor,@now,'Customer accepted a quote.')
                """,("@id",Guid.NewGuid().ToString()),("@request",requestId.ToString()),("@actor",actor.ToString()),("@now",now)))await history.ExecuteNonQueryAsync(ct);
            await using(var others=Cmd(c,tx,"UPDATE service_request_quotes SET status='Withdrawn',decided_at=@now,decision_reason='Another quote was accepted.' WHERE request_id=@request AND status='Pending'",("@request",requestId.ToString()),("@now",now)))await others.ExecuteNonQueryAsync(ct);
        }
        var result=await ReadOne(c,tx,quoteId,now,ct);await tx.CommitAsync(ct);return decision=="Withdrawn"?result with {PropertyAddress=null}:result;
    }
}
