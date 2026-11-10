using Identity.Application.Features.Auth;
using Identity.Application.Features.Signup;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Persistence;

internal static class AccountSql
{
 internal static string Table(string application)=>application switch
 {"App"=>"app_accounts","Tech"=>"tech_accounts","Admin"=>"admin_accounts",_=>throw new ApplicationLoginException(400,"Application must be App, Tech or Admin.")};
 internal static User Read(MySqlDataReader r,string application)=>new()
 {
  Id=r.GetGuid(r.GetOrdinal("id")),UserName=r.GetString("user_name"),Email=r.GetString("email"),Application=application,
  PasswordHash=Text(r,"password_hash"),IsActive=r.GetBoolean("is_active")&&!r.GetBoolean("is_deleted"),
  FirstName=Text(r,"first_name"),LastName=Text(r,"last_name"),Phone=Text(r,"phone"),
  ProviderEnrollmentRequestedAtUtc=r.IsDBNull(r.GetOrdinal("provider_enrollment_requested_at"))?null:DateTime.SpecifyKind(r.GetDateTime("provider_enrollment_requested_at"),DateTimeKind.Utc)
 };
 private static string? Text(MySqlDataReader r,string col)=>r.IsDBNull(r.GetOrdinal(col))?null:r.GetString(col);
 internal static async Task<User> InsertAsync(MySqlConnection c,MySqlTransaction tx,string application,SignupAccount a,CancellationToken ct,DateTime? now=null,string? username=null)
 {
  var id=Guid.NewGuid();var name=username??"user_"+id.ToString("N");var created=now??DateTime.UtcNow;
  await using var command=new MySqlCommand($"""
   INSERT INTO {Table(application)}(id,user_name,email,password_hash,is_active,is_deleted,created_at,first_name,last_name,phone,updated_at,provider_enrollment_requested_at)
   VALUES(@id,@name,@email,@hash,1,0,@now,@first,@last,@phone,@now,@consent)
   """,c,tx);
  foreach(var(k,v)in new(string,object?)[]{("@id",id.ToString()),("@name",name),("@email",a.Email),("@hash",a.PasswordHash),("@now",created),("@first",a.FirstName),("@last",a.LastName),("@phone",a.Phone),("@consent",application=="Tech"?created:null)})command.Parameters.AddWithValue(k,v??DBNull.Value);
  try{await command.ExecuteNonQueryAsync(ct);}catch(MySqlException ex)when(ex.Number==1062){throw new SignupException(409,"An account already exists in this application. Sign in to continue.");}
  return new(){Id=id,UserName=name,Email=a.Email,PasswordHash=a.PasswordHash,IsActive=true,Application=application,FirstName=a.FirstName,LastName=a.LastName,Phone=a.Phone,ProviderEnrollmentRequestedAtUtc=application=="Tech"?created:null};
 }
}
public sealed class ApplicationAccountRepository(IConfiguration configuration):IApplicationAccountRepository
{
 public async Task<User?> FindAsync(string app,string login,CancellationToken ct)
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);
  await using var cmd=new MySqlCommand($"SELECT * FROM {AccountSql.Table(app)} WHERE user_name=@login OR email=@login LIMIT 2",c);cmd.Parameters.AddWithValue("@login",login.Trim());
  await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;var user=AccountSql.Read(r,app);return await r.ReadAsync(ct)?null:user;
 }
 public async Task<User?> FindByIdAsync(string app,Guid id,CancellationToken ct)
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);
  await using var cmd=new MySqlCommand($"SELECT * FROM {AccountSql.Table(app)} WHERE id=@id",c);cmd.Parameters.AddWithValue("@id",id.ToString());await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?AccountSql.Read(r,app):null;
 }
 public async Task<User> CreateAsync(string app,SignupAccount account,CancellationToken ct)
 {
  if(app=="Admin")throw new SignupException(403,"Use authorized administrator registration to create an Admin account.");
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  var user=await AccountSql.InsertAsync(c,tx,app,account,ct);await tx.CommitAsync(ct);return user;
 }
}
