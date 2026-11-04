using System.Data;
using Identity.Application.Features.Signup;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Persistence;
public sealed class SignupRepository(IConfiguration configuration):ISignupRepository
{
 private async Task<MySqlConnection> Open(CancellationToken ct)
 {var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
 private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params(string,object)[] args)
 {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v);return cmd;}
 public async Task<User> CreateAsync(SignupAccount account,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
  // A durable unique normalized-email reservation serializes concurrent signup attempts.
  // Legacy registrations are also checked; no existing account is implicitly linked.
  var id=Guid.NewGuid();var name="user_"+id.ToString("N");
  try
  {
   await using(var reserve=Cmd(c,tx,"INSERT INTO identity_signup_emails(normalized_email,user_id)VALUES(@email,@id)",("@email",account.Email),("@id",id.ToString())))await reserve.ExecuteNonQueryAsync(ct);
   await using(var check=Cmd(c,tx,"SELECT 1 FROM identity_users WHERE LOWER(TRIM(email))=@email OR user_name=@email LIMIT 1",("@email",account.Email)))
    if(await check.ExecuteScalarAsync(ct) is not null)throw new SignupException(409,"An account already exists. Sign in to continue.");
   await using(var user=Cmd(c,tx,"INSERT INTO identity_users(id,user_name,email,password_hash,is_active,is_deleted,created_at)VALUES(@id,@name,@email,@password,1,0,UTC_TIMESTAMP(6))",("@id",id.ToString()),("@name",name),("@email",account.Email),("@password",account.PasswordHash)))await user.ExecuteNonQueryAsync(ct);
   await using(var profile=Cmd(c,tx,"INSERT INTO identity_user_profiles(user_id,first_name,last_name,phone,updated_at)VALUES(@id,@first,@last,@phone,UTC_TIMESTAMP(6))",("@id",id.ToString()),("@first",account.FirstName),("@last",account.LastName),("@phone",account.Phone)))await profile.ExecuteNonQueryAsync(ct);
   await tx.CommitAsync(ct);
   return new User{Id=id,UserName=name,Email=account.Email,PasswordHash=account.PasswordHash,IsActive=true};
  }
  catch(MySqlException ex)when(ex.Number==1062){throw new SignupException(409,"An account already exists. Sign in to continue.");}
 }
 public async Task<User?> FindByEmailAsync(string email,CancellationToken ct)
 {
  await using var c=await Open(ct);
  await using var cmd=Cmd(c,null,"SELECT id,user_name,email,password_hash,is_active,is_deleted FROM identity_users WHERE LOWER(TRIM(email))=@email LIMIT 2",("@email",email.Trim().ToLowerInvariant()));
  await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
  var user=new User{Id=r.GetGuid(0),UserName=r.GetString(1),Email=r.GetString(2),PasswordHash=r.IsDBNull(3)?null:r.GetString(3),IsActive=r.GetBoolean(4)&&!r.GetBoolean(5)};
  // Legacy duplicate emails are ambiguous; never choose an arbitrary account.
  return await r.ReadAsync(ct)?null:user;
 }
}
