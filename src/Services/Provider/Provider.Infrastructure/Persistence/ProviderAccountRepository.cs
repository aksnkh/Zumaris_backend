using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Persistence;

public sealed class ProviderAccountRepository(IConfiguration configuration) : IProviderAccountRepository
{
    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));
        try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; }
    }
    private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params (string,object?)[] values)
    {
        var cmd=new MySqlCommand(sql,c,tx);
        foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        return cmd;
    }
    private static ProviderWorkflowException Missing()=>new(404,"Provider profile or document not found.");
    private static ProviderWorkflowException Conflict(string message)=>new(409,message);
    private static async Task<Guid> Lock(MySqlConnection c,MySqlTransaction tx,Guid user,CancellationToken ct)
    {
        await using var cmd=Cmd(c,tx,"SELECT id FROM providers WHERE user_id=@user FOR UPDATE",("@user",user.ToString()));
        var value=await cmd.ExecuteScalarAsync(ct);return value is null or DBNull?throw Missing():Guid.Parse(value.ToString()!);
    }
    public async Task<ProviderAccount?> GetAsync(Guid userId,CancellationToken ct)
    {
        await using var c=await Open(ct);
        await using var cmd=Cmd(c,null,"SELECT id FROM providers WHERE user_id=@user",("@user",userId.ToString()));
        var value=await cmd.ExecuteScalarAsync(ct);return value is null or DBNull?null:await ByIdAsync(Guid.Parse(value.ToString()!),ct);
    }
    public async Task<ProviderAccount?> ByIdAsync(Guid id,CancellationToken ct)
    {
        await using var c=await Open(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using var cmd=Cmd(c,tx,"""
          SELECT p.*,COALESCE(o.revision,1) AS revision,o.review_reason,o.reviewed_revision
          FROM providers p LEFT JOIN provider_onboarding o ON o.provider_id=p.id WHERE p.id=@id
          """,("@id",id.ToString()));
        Guid user;string name;bool active,verified;int revision;string? reason;int? reviewed;
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct))return null;
            user=r.GetGuid(r.GetOrdinal("user_id"));name=r.GetString("display_name");active=r.GetBoolean("is_active");
            verified=r.GetBoolean("is_verified");revision=r.GetInt32("revision");reason=r.IsDBNull(r.GetOrdinal("review_reason"))?null:r.GetString("review_reason");
            reviewed=r.IsDBNull(r.GetOrdinal("reviewed_revision"))?null:r.GetInt32("reviewed_revision");
        }
        var categories=new List<int>();
        await using(var q=Cmd(c,tx,"SELECT category_id FROM provider_category_qualifications WHERE provider_id=@id AND is_qualified=1 ORDER BY category_id",("@id",id.ToString())))
        await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))categories.Add(r.GetInt32(0));
        var documents=new List<ProviderDocument>();
        await using(var q=Cmd(c,tx,"SELECT id,kind,file_name,content_type,created_at FROM provider_documents WHERE provider_id=@id ORDER BY created_at,id",("@id",id.ToString())))
        await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))documents.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),DateTime.SpecifyKind(r.GetDateTime(4),DateTimeKind.Utc)));
        await tx.CommitAsync(ct);
        var status=verified&&reviewed==revision&&documents.Count>0?"Approved":documents.Count==0?"Draft":reviewed==revision?"Rejected":"PendingReview";
        return new(id,user,name,active,verified&&status=="Approved",status,revision,reason,categories,documents);
    }
    public async Task<ProviderAccount> SaveAsync(Guid user,SaveProviderRequest input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.DisplayName)||input.CategoryIds is null||input.CategoryIds.Length is <1 or >100||input.CategoryIds.Any(x=>x<1))
            throw new ProviderWorkflowException(400,"Provide a display name and positive category IDs.");
        await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);
        Guid id;string? previous;
        await using(var cmd=Cmd(c,tx,"SELECT id,display_name FROM providers WHERE user_id=@user FOR UPDATE",("@user",user.ToString())))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {if(await r.ReadAsync(ct)){id=r.GetGuid(0);previous=r.GetString(1);}else{id=Guid.NewGuid();previous=null;}}
        if(previous is null)
        {
            try
            {
                await using var cmd=Cmd(c,tx,"INSERT INTO providers(id,user_id,display_name)VALUES(@id,@user,@name)",("@id",id.ToString()),("@user",user.ToString()),("@name",input.DisplayName.Trim()));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch(MySqlException ex)when(ex.Number==1062){throw Conflict("A provider profile already exists. Reload before retrying.");}
        }
        else
        {
            await using var cmd=Cmd(c,tx,"UPDATE providers SET is_verified=IF(display_name=@name,is_verified,0),display_name=@name WHERE id=@id",("@id",id.ToString()),("@name",input.DisplayName.Trim()));await cmd.ExecuteNonQueryAsync(ct);
        }
        await using(var cmd=Cmd(c,tx,"INSERT IGNORE INTO provider_onboarding(provider_id)VALUES(@id)",("@id",id.ToString())))await cmd.ExecuteNonQueryAsync(ct);
        if(previous is not null&&previous!=input.DisplayName.Trim())
        {await using var cmd=Cmd(c,tx,"UPDATE provider_onboarding SET revision=revision+1,review_reason=NULL WHERE provider_id=@id",("@id",id.ToString()));await cmd.ExecuteNonQueryAsync(ct);}
        await using(var cmd=Cmd(c,tx,"UPDATE provider_category_qualifications SET is_qualified=0 WHERE provider_id=@id",("@id",id.ToString())))await cmd.ExecuteNonQueryAsync(ct);
        foreach(var category in input.CategoryIds.Distinct())
        {
            await using var cmd=Cmd(c,tx,"INSERT INTO provider_category_qualifications(provider_id,category_id,is_qualified)VALUES(@id,@category,1) ON DUPLICATE KEY UPDATE is_qualified=1",("@id",id.ToString()),("@category",category));await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);return (await ByIdAsync(id,ct))!;
    }
    public async Task<ProviderDocument> AddDocumentAsync(Guid user,ProviderDocumentUpload input,CancellationToken ct)
    {
        await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var provider=await Lock(c,tx,user,ct);
        await using(var count=Cmd(c,tx,"SELECT COUNT(*) FROM provider_documents WHERE provider_id=@id",("@id",provider.ToString())))
            if(Convert.ToInt32(await count.ExecuteScalarAsync(ct))>=20)throw Conflict("A maximum of 20 documents is allowed.");
        var doc=new ProviderDocument(Guid.NewGuid(),input.Kind,input.FileName,input.ContentType,DateTime.UtcNow);
        await using(var cmd=Cmd(c,tx,"INSERT INTO provider_documents(id,provider_id,kind,file_name,content_type,content,created_at)VALUES(@id,@provider,@kind,@name,@type,@content,@now)",
            ("@id",doc.Id.ToString()),("@provider",provider.ToString()),("@kind",doc.Kind),("@name",doc.FileName),("@type",doc.ContentType),("@content",input.Content),("@now",doc.CreatedAtUtc)))await cmd.ExecuteNonQueryAsync(ct);
        await using(var cmd=Cmd(c,tx,"UPDATE providers SET is_verified=0 WHERE id=@id",("@id",provider.ToString())))await cmd.ExecuteNonQueryAsync(ct);
        await using(var cmd=Cmd(c,tx,"INSERT INTO provider_onboarding(provider_id,revision)VALUES(@id,2) ON DUPLICATE KEY UPDATE revision=revision+1,review_reason=NULL",("@id",provider.ToString())))await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return doc;
    }
    public async Task<PrivateProviderDocument?> DocumentAsync(Guid? owner,Guid providerId,Guid documentId,CancellationToken ct)
    {
        await using var c=await Open(ct);await using var cmd=Cmd(c,null,"""
          SELECT d.file_name,d.content_type,d.content FROM provider_documents d JOIN providers p ON p.id=d.provider_id
          WHERE d.id=@doc AND p.id=@provider AND (@owner IS NULL OR p.user_id=@owner)
          """,("@doc",documentId.ToString()),("@provider",providerId.ToString()),("@owner",owner?.ToString()));
        await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetString(0),r.GetString(1),(byte[])r[2]):null;
    }
    public async Task<ProviderPage> PendingAsync(int page,int size,CancellationToken ct)
    {
        await using var c=await Open(ct);
        const string filter="is_verified=0 AND EXISTS(SELECT 1 FROM provider_documents d WHERE d.provider_id=p.id)";
        await using var count=Cmd(c,null,"SELECT COUNT(*) FROM providers p WHERE "+filter);var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd=Cmd(c,null,"SELECT id FROM providers p WHERE "+filter+" ORDER BY id LIMIT @size OFFSET @offset",("@size",size),("@offset",(long)(page-1)*size));
        var ids=new List<Guid>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))ids.Add(r.GetGuid(0));
        var list=new List<ProviderAccount>();foreach(var id in ids){var item=await ByIdAsync(id,ct);if(item is not null)list.Add(item);}
        return new(list,page,size,total);
    }
    public async Task<ProviderAccount> ReviewAsync(Guid admin,Guid id,ReviewProviderRequest input,CancellationToken ct)
    {
        await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var p=Cmd(c,tx,"SELECT id FROM providers WHERE id=@id FOR UPDATE",("@id",id.ToString())))if(await p.ExecuteScalarAsync(ct) is null)throw Missing();
        int revision;
        await using(var cmd=Cmd(c,tx,"SELECT revision FROM provider_onboarding WHERE provider_id=@id FOR UPDATE",("@id",id.ToString())))revision=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        if(revision!=input.Revision)throw Conflict("The profile or documents changed. Reload before reviewing.");
        await using(var cmd=Cmd(c,tx,"SELECT COUNT(*) FROM provider_documents WHERE provider_id=@id",("@id",id.ToString())))
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==0)throw Conflict("Documents must be uploaded before review.");
        await using(var cmd=Cmd(c,tx,"UPDATE providers SET is_verified=@approved WHERE id=@id",("@id",id.ToString()),("@approved",input.Approved)))await cmd.ExecuteNonQueryAsync(ct);
        await using(var cmd=Cmd(c,tx,"UPDATE provider_onboarding SET reviewed_revision=revision,reviewed_by=@admin,reviewed_at=@now,review_reason=@reason WHERE provider_id=@id",("@id",id.ToString()),("@admin",admin.ToString()),("@now",DateTime.UtcNow),("@reason",input.Reason?.Trim())))await cmd.ExecuteNonQueryAsync(ct);
        await using(var audit=Cmd(c,tx,"INSERT INTO provider_verification_history(id,provider_id,revision,administrator_id,approved,reason,occurred_at)VALUES(@event,@id,@revision,@admin,@approved,@reason,@now)",("@event",Guid.NewGuid().ToString()),("@id",id.ToString()),("@revision",revision),("@admin",admin.ToString()),("@approved",input.Approved),("@reason",input.Reason?.Trim()),("@now",DateTime.UtcNow)))await audit.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return (await ByIdAsync(id,ct))!;
    }
    public async Task<CustomerReview> AddReviewAsync(Guid customer,CompletedWork work,CustomerReviewRequest input,CancellationToken ct)
    {
        if(work.CustomerId!=customer||work.Status!="Completed")throw new ProviderWorkflowException(403,"Only the customer of a completed job may review it.");
        if(input.Rating is <1 or >5||string.IsNullOrWhiteSpace(input.Comment)||input.Comment.Length>2000)throw new ProviderWorkflowException(400,"Provide a rating from 1 to 5 and a review of up to 2000 characters.");
        await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var cmd=Cmd(c,tx,"SELECT id FROM providers WHERE id=@id FOR UPDATE",("@id",work.ProviderId.ToString())))if(await cmd.ExecuteScalarAsync(ct) is null)throw Missing();
        var item=new CustomerReview(work.RequestId,work.ProviderId,input.Rating,input.Comment.Trim(),DateTime.UtcNow);
        await using(var cmd=Cmd(c,tx,"SELECT provider_id,customer_id,rating,comment,created_at FROM provider_reviews WHERE request_id=@id",("@id",work.RequestId.ToString())))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(await r.ReadAsync(ct))
            {
                if(r.GetGuid(0)!=work.ProviderId||r.GetGuid(1)!=customer||r.GetInt32(2)!=item.Rating||r.GetString(3)!=item.Comment)throw Conflict("A review has already been submitted for this job.");
                return item with {CreatedAtUtc=DateTime.SpecifyKind(r.GetDateTime(4),DateTimeKind.Utc)};
            }
        }
        await using(var cmd=Cmd(c,tx,"INSERT INTO provider_reviews(request_id,provider_id,customer_id,rating,comment,created_at)VALUES(@id,@provider,@customer,@rating,@comment,@now)",("@id",item.RequestId.ToString()),("@provider",item.ProviderId.ToString()),("@customer",customer.ToString()),("@rating",item.Rating),("@comment",item.Comment),("@now",item.CreatedAtUtc)))await cmd.ExecuteNonQueryAsync(ct);
        await using(var cmd=Cmd(c,tx,"UPDATE providers SET rating=(SELECT ROUND(AVG(rating),2) FROM provider_reviews WHERE provider_id=@id) WHERE id=@id",("@id",work.ProviderId.ToString())))await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return item;
    }
    public async Task<IReadOnlyList<CustomerReview>> ReviewsAsync(Guid provider,int page,int size,CancellationToken ct)
    {
        await using var c=await Open(ct);await using var cmd=Cmd(c,null,"SELECT request_id,rating,comment,created_at FROM provider_reviews WHERE provider_id=@id ORDER BY created_at DESC,request_id DESC LIMIT @size OFFSET @offset",("@id",provider.ToString()),("@size",size),("@offset",(long)(page-1)*size));
        var list=new List<CustomerReview>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))list.Add(new(r.GetGuid(0),provider,r.GetInt32(1),r.GetString(2),DateTime.SpecifyKind(r.GetDateTime(3),DateTimeKind.Utc)));return list;
    }
}
