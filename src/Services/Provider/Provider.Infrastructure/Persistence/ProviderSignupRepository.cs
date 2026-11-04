using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Persistence;
public sealed class ProviderSignupRepository(IConfiguration configuration,IProviderAccountRepository accounts):IProviderSignupRepository
{
 private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction tx,string sql,params(string,object?)[] args)
 {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);return cmd;}
 public async Task<ProviderSignupResult> CompleteAsync(Guid actor,string displayName,string? termsVersion,string? privacyVersion,CancellationToken ct)
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
  var newId=Guid.NewGuid();
  // The unique Identity user key also serializes concurrent first signup attempts.
  await using(var create=Cmd(c,tx,"INSERT INTO providers(id,user_id,display_name)VALUES(@id,@user,@name) ON DUPLICATE KEY UPDATE user_id=providers.user_id",("@id",newId.ToString()),("@user",actor.ToString()),("@name",displayName)))await create.ExecuteNonQueryAsync(ct);
  Guid id;
  await using(var find=Cmd(c,tx,"SELECT id FROM providers WHERE user_id=@user FOR UPDATE",("@user",actor.ToString())))id=Guid.Parse((await find.ExecuteScalarAsync(ct))!.ToString()!);
  await using(var onboard=Cmd(c,tx,"INSERT IGNORE INTO provider_onboarding(provider_id)VALUES(@id)",("@id",id.ToString())))await onboard.ExecuteNonQueryAsync(ct);
  var now=DateTime.UtcNow;
  await using(var details=Cmd(c,tx,"""
   INSERT INTO provider_signup_details(provider_id,accepted_at,terms_version,privacy_version)
   VALUES(@id,@now,@terms,@privacy)
   ON DUPLICATE KEY UPDATE provider_id=provider_signup_details.provider_id
   """,("@id",id.ToString()),("@now",now),("@terms",termsVersion),("@privacy",privacyVersion)))await details.ExecuteNonQueryAsync(ct);
  DateTime accepted;string? terms,privacy;
  await using(var read=Cmd(c,tx,"SELECT accepted_at,terms_version,privacy_version FROM provider_signup_details WHERE provider_id=@id",("@id",id.ToString())))
  await using(var r=await read.ExecuteReaderAsync(ct)){await r.ReadAsync(ct);accepted=DateTime.SpecifyKind(r.GetDateTime(0),DateTimeKind.Utc);terms=r.IsDBNull(1)?null:r.GetString(1);privacy=r.IsDBNull(2)?null:r.GetString(2);}
  await tx.CommitAsync(ct);
  var account=await accounts.ByIdAsync(id,ct)??throw new ProviderWorkflowException(503,"Provider registration could not be loaded. Sign in and retry.");
  return new(account,accepted,terms,privacy);
 }
}
