using System.Data;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Features.Auth;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Persistence;
public sealed class ExternalLoginAdoNetRepository(IConfiguration configuration):IExternalLoginRepository
{
 private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params(string,object)[] args)
 {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v);return cmd;}
 private static async Task<User?> Find(MySqlConnection c,MySqlTransaction? tx,string app,string subject,CancellationToken ct)
 {
  await using var cmd=Cmd(c,tx,$"SELECT u.* FROM identity_external_logins e JOIN {AccountSql.Table(app)} u ON u.id=e.user_id WHERE e.application=@app AND e.provider='Google' AND e.provider_subject=@subject",("@app",app),("@subject",subject));
  await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?AccountSql.Read(r,app):null;
 }
 public async Task<User> FindOrCreateGoogleAsync(VerifiedGoogleIdentity identity,CancellationToken ct,string application="App")
 {
  var table=AccountSql.Table(application);await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);
  var existing=await Find(c,null,application,identity.Subject,ct);if(existing is not null)return existing;
  // Tech/Admin registration must be explicit; Google cannot bypass consent or admin authorization.
  if(application!="App")throw new ApplicationLoginException(403,"Register an account in this application and explicitly link Google before signing in with Google.");
  var name="google_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Subject))).ToLowerInvariant()[..32];
  try
  {
   await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
   await using(var check=Cmd(c,tx,$"SELECT id FROM {table} WHERE email=@email OR user_name=@name LIMIT 1",("@email",identity.Email),("@name",name)))
   if(await check.ExecuteScalarAsync(ct)is not null){var winner=await Find(c,tx,application,identity.Subject,ct);if(winner is null)throw new ExternalLoginConflictException();await tx.CommitAsync(ct);return winner;}
   var id=Guid.NewGuid();await using(var insert=Cmd(c,tx,$"INSERT INTO {table}(id,user_name,email,password_hash,created_at)VALUES(@id,@name,@email,NULL,UTC_TIMESTAMP(6))",("@id",id.ToString()),("@name",name),("@email",identity.Email)))await insert.ExecuteNonQueryAsync(ct);
   await Link(c,tx,id,application,identity.Subject,ct);await tx.CommitAsync(ct);
   return new(){Id=id,UserName=name,Email=identity.Email,PasswordHash=null,IsActive=true,Application=application};
  }
  catch(MySqlException ex)when(ex.Number==1062){return await Find(c,null,application,identity.Subject,ct)??throw new ExternalLoginConflictException();}
 }
 private static async Task Link(MySqlConnection c,MySqlTransaction tx,Guid id,string app,string subject,CancellationToken ct)
 {await using var cmd=Cmd(c,tx,"INSERT INTO identity_external_logins(id,user_id,application,provider,provider_subject,created_at)VALUES(@id,@user,@app,'Google',@subject,UTC_TIMESTAMP(6))",("@id",Guid.NewGuid().ToString()),("@user",id.ToString()),("@app",app),("@subject",subject));await cmd.ExecuteNonQueryAsync(ct);}
 public async Task LinkGoogleAsync(Guid id,VerifiedGoogleIdentity identity,CancellationToken ct,string application="App")
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  User user;await using(var cmd=Cmd(c,tx,$"SELECT * FROM {AccountSql.Table(application)} WHERE id=@id FOR UPDATE",("@id",id.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))user=await r.ReadAsync(ct)?AccountSql.Read(r,application):throw new UnauthorizedAccessException("Account unavailable.");
  if(!user.IsActive)throw new UnauthorizedAccessException("Account unavailable.");if(!string.Equals(user.Email,identity.Email,StringComparison.OrdinalIgnoreCase))throw new ExternalLoginConflictException();
  var current=await Find(c,tx,application,identity.Subject,ct);if(current is not null){if(current.Id!=id)throw new ExternalLoginConflictException();await tx.CommitAsync(ct);return;}
  try{await Link(c,tx,id,application,identity.Subject,ct);await tx.CommitAsync(ct);}catch(MySqlException ex)when(ex.Number==1062){throw new ExternalLoginConflictException();}
 }
}
