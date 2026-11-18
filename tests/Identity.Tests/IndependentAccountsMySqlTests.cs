using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Signup;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;
namespace Identity.Tests;
public sealed class IndependentMySqlFactAttribute:FactAttribute
{public IndependentMySqlFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZUMARIS_INDEPENDENT_IDENTITY_TEST_CONNECTION")))Skip="Requires disposable loopback zumaris_independent_identity_test database.";}}
[CollectionDefinition("Independent Identity SQL",DisableParallelization=true)]public sealed class IndependentIdentitySqlCollection{}
[Collection("Independent Identity SQL")]
public class IndependentAccountsMySqlTests
{
 private static async Task<(string Cs,IConfiguration Config)> Reset()
 {
  var cs=Environment.GetEnvironmentVariable("ZUMARIS_INDEPENDENT_IDENTITY_TEST_CONNECTION")!;var parsed=new MySqlConnectionStringBuilder(cs){AllowUserVariables=true};
  Assert.Contains(parsed.Server,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("zumaris_independent_identity_test",parsed.Database);
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  var sql=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/008_reset_independent_accounts.sql"))).Replace("zumaris_identity","zumaris_independent_identity_test");
  await using var c=new MySqlConnection(parsed.ConnectionString);await c.OpenAsync();await using var cmd=new MySqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();
  return(parsed.ConnectionString,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",parsed.ConnectionString}}).Build());
 }
 private static SignupAccount Input(string email="same@example.test")=>new("Same","Person",email,"+447700900123",new PasswordHasher().Hash("Separate-password-123"));
 [IndependentMySqlFact]
 public async Task Recovery_is_single_use_scoped_revokes_sessions_and_rejects_stale_login_credentials()
 {
  var(cs,cfg)=await Reset();
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  await using(var c=new MySqlConnection(cs)){await c.OpenAsync();await using var migration=new MySqlCommand("DROP TABLE IF EXISTS identity_password_resets;"+(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/010_password_recovery.sql"))).Replace("zumaris_identity","zumaris_independent_identity_test"),c);await migration.ExecuteNonQueryAsync();}
  var accounts=new ApplicationAccountRepository(cfg);var app=await accounts.CreateAsync("App",Input(),default);var tech=await accounts.CreateAsync("Tech",Input(),default);var createdAdmin=await new AdministratorAccountRepository(cfg).CreateAsync(Input(),null,DateTime.UtcNow,default);var admin=(await accounts.FindByIdAsync("Admin",createdAdmin.UserId,default))!;
  var sessions=new SessionAdoNetRepository(cfg);var now=DateTime.UtcNow;
  var a=new AuthSession(Guid.NewGuid(),app.Id,app.UserName,now,now.AddHours(1),null){Application="App"};
  var t=new AuthSession(Guid.NewGuid(),tech.Id,tech.UserName,now,now.AddHours(1),null){Application="Tech",ProviderId=Guid.NewGuid()};
  var m=new AuthSession(Guid.NewGuid(),admin.Id,admin.UserName,now,now.AddHours(1),null){Application="Admin"};
  await sessions.CreateAsync(a,new string('a',64),default,app.PasswordHash);await sessions.CreateAsync(t,new string('b',64),default,tech.PasswordHash);await sessions.CreateAsync(m,new string('c',64),default,admin.PasswordHash);
  var recovery=new PasswordRecoveryRepository(cfg);var hash=PasswordRecoveryService.TokenHash(new string('x',43));
  await accounts.CreateAsync("App",Input("tést@example.test"),default);
  Assert.False(await recovery.Issue(new(Guid.NewGuid(),"App","test@example.test",new string('d',64),"protected-test",now,now.AddMinutes(15),60),default));
  Assert.True(await recovery.Issue(new(Guid.NewGuid(),"App",app.Email,hash,"protected-test",now,now.AddMinutes(15),60),default));
  Assert.False(await recovery.Issue(new(Guid.NewGuid(),"App",app.Email,new string('d',64),"protected-test",now,now.AddMinutes(15),60),default));
  var next=new PasswordHasher().Hash("New-password-123");
  Assert.False(await recovery.Consume("Tech",hash,next,now,default));
  var outcomes=await Task.WhenAll(recovery.Consume("App",hash,next,now,default),recovery.Consume("App",hash,next,now,default));Assert.Single(outcomes,x=>x);
  Assert.False(await sessions.IsActiveAsync(a.Id,app.Id));Assert.True(await sessions.IsActiveAsync(t.Id,tech.Id));Assert.True(await sessions.IsActiveAsync(m.Id,admin.Id));
  Assert.False(await recovery.Consume("App",hash,next,now,default));
  var stale=a with{Id=Guid.NewGuid()};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>sessions.CreateAsync(stale,new string('e',64),default,app.PasswordHash));
  var fresh=a with{Id=Guid.NewGuid()};await sessions.CreateAsync(fresh,new string('f',64),default,next);Assert.True(await sessions.IsActiveAsync(fresh.Id,app.Id));
 }
 [IndependentMySqlFact]
 public async Task Three_account_tables_allow_same_email_without_sharing_profiles_credentials_or_admin_access()
 {
  var(_,cfg)=await Reset();var accounts=new ApplicationAccountRepository(cfg);var adminRepo=new AdministratorAccountRepository(cfg);
  var app=await accounts.CreateAsync("App",Input(),default);var tech=await accounts.CreateAsync("Tech",Input(),default);var admin=await adminRepo.CreateAsync(Input(),null,DateTime.UtcNow,default);
  Assert.Equal(3,new[]{app.Id,tech.Id,admin.UserId}.Distinct().Count());Assert.Equal(app.Email,app.UserName);Assert.Equal(tech.Email,tech.UserName);Assert.Equal(admin.Email,admin.UserName);
  Assert.Equal(app.Id,(await accounts.FindAsync("App","SAME@example.test",default))!.Id);Assert.Equal(tech.Id,(await accounts.FindAsync("Tech","SAME@example.test",default))!.Id);Assert.Equal(admin.UserId,(await accounts.FindAsync("Admin","same@example.test",default))!.Id);
  Assert.Null(await accounts.FindByIdAsync("App",tech.Id,default));Assert.Null(await accounts.FindByIdAsync("Tech",admin.UserId,default));
  var admins=new PlatformAdminRepository(cfg);Assert.False(await admins.IsAdminAsync(app.Id,default));Assert.False(await admins.IsAdminAsync(tech.Id,default));Assert.True(await admins.IsAdminAsync(admin.UserId,default));
  var profiles=new ProfileAdoNetRepository(cfg);await profiles.UpdateAsync(tech.Id,new("Tech","Changed","+447700900124"),DateTime.UtcNow,default);
  Assert.Equal("Same",(await profiles.GetAsync(app.Id,default))!.FirstName);Assert.Equal("Tech",(await profiles.GetAsync(tech.Id,default))!.FirstName);Assert.Equal("Same",(await profiles.GetAsync(admin.UserId,default))!.FirstName);
  Assert.Equal(409,(await Assert.ThrowsAsync<SignupException>(()=>accounts.CreateAsync("App",Input(),default))).Status);
  Assert.Equal(403,(await Assert.ThrowsAsync<SignupException>(()=>adminRepo.CreateAsync(Input("bad@example.test"),app.Id,DateTime.UtcNow,default))).Status);
 }
 [IndependentMySqlFact]
 public async Task Email_username_supports_full_length_and_sessions_use_user_id()
 {
  var(cs,cfg)=await Reset();
  var email=new string('a',64)+"@"+new string('b',63)+"."+new string('c',63)+"."+new string('d',56)+".test";
  Assert.Equal(254,email.Length);
  var accounts=new ApplicationAccountRepository(cfg);var user=await accounts.CreateAsync("App",Input(email),default);
  Assert.Equal(email,user.UserName);Assert.Equal(user.Id,(await accounts.FindAsync("App",email,default))!.Id);
  var sessions=new SessionAdoNetRepository(cfg);var now=DateTime.UtcNow;
  var session=new AuthSession(Guid.NewGuid(),user.Id,user.UserName,now,now.AddHours(1),null){Application="App"};
  await sessions.CreateAsync(session,new string('a',64));Assert.True(await sessions.IsActiveAsync(session.Id,user.Id));
  // The patch for installations that already applied 008 must retain data.
  await using var c=new MySqlConnection(cs);await c.OpenAsync();
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Zumaris.sln")))root=root.Parent;
  var patch=(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/identity/009_email_username_capacity.sql"))).Replace("zumaris_identity","zumaris_independent_identity_test");
  await using(var cmd=new MySqlCommand(patch,c))await cmd.ExecuteNonQueryAsync();
  Assert.Equal(user.Id,(await accounts.FindAsync("App",email,default))!.Id);
  await using(var change=new MySqlCommand("UPDATE app_accounts SET user_name='future-name@example.test' WHERE id=@id",c))
  {change.Parameters.AddWithValue("@id",user.Id.ToString());await change.ExecuteNonQueryAsync();}
  Assert.Null(await accounts.FindAsync("App","future-name@example.test",default));
  Assert.Equal(user.Id,(await accounts.FindAsync("App",email,default))!.Id);
  Assert.True(await sessions.IsActiveAsync(session.Id,user.Id));
 }
 [IndependentMySqlFact]
 public async Task Concurrent_registration_is_unique_inside_each_table_but_does_not_reserve_another_sides_email()
 {
  var(_,cfg)=await Reset();var accounts=new ApplicationAccountRepository(cfg);
  async Task<Guid?> Create(string app){try{return(await accounts.CreateAsync(app,Input(),default)).Id;}catch(SignupException ex){Assert.Equal(409,ex.Status);return null;}}
  Assert.Single(await Task.WhenAll(Create("App"),Create("App")),x=>x.HasValue);
  Assert.Single(await Task.WhenAll(Create("Tech"),Create("Tech")),x=>x.HasValue);
  var admins=new AdministratorAccountRepository(cfg);
  async Task<Guid?> Bootstrap(string email){try{return(await admins.CreateAsync(Input(email),null,DateTime.UtcNow,default)).UserId;}catch(SignupException ex){Assert.Equal(409,ex.Status);return null;}}
  Assert.Single(await Task.WhenAll(Bootstrap("same@example.test"),Bootstrap("other@example.test")),x=>x.HasValue);
 }
 [IndependentMySqlFact]
 public async Task Sessions_are_bound_to_the_account_table_rotation_and_replay_revocation_are_preserved()
 {
  var(_,cfg)=await Reset();var accounts=new ApplicationAccountRepository(cfg);var app=await accounts.CreateAsync("App",Input(),default);var tech=await accounts.CreateAsync("Tech",Input(),default);
  var sessions=new SessionAdoNetRepository(cfg);var now=DateTime.UtcNow;
  var s=new AuthSession(Guid.NewGuid(),tech.Id,tech.UserName,now,now.AddHours(1),null){Application="Tech",ProviderId=Guid.NewGuid()};
  var old=new string('a',64);var next=new string('b',64);await sessions.CreateAsync(s,old);
  Assert.Equal("Tech",(await sessions.FindByTokenAsync(old))!.Application);Assert.True(await sessions.IsActiveAsync(s.Id,tech.Id));Assert.False(await sessions.IsActiveAsync(s.Id,app.Id));
  Assert.True(await sessions.RotateAsync(s.Id,old,next));Assert.False(await sessions.RotateAsync(s.Id,old,next));Assert.False(await sessions.IsActiveAsync(s.Id,tech.Id));
  var invalid=new AuthSession(Guid.NewGuid(),tech.Id,tech.UserName,now,now.AddHours(1),null){Application="App"};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>sessions.CreateAsync(invalid,new string('c',64)));Assert.Null(await sessions.FindByIdAsync(invalid.Id,tech.Id));
  var tokens=new JwtTokenService(new JwtSettings{Key=AuthFactory.TestKey,Issuer="tests",Audience="clients"});Assert.Throws<InvalidOperationException>(()=>tokens.CreateAccessToken(tech,Guid.NewGuid(),now.AddHours(1),"App"));
 }
 [IndependentMySqlFact]
 public async Task Google_links_are_scoped_and_cannot_create_or_take_over_Tech_or_Admin_accounts()
 {
  var(_,cfg)=await Reset();var accounts=new ApplicationAccountRepository(cfg);var app=await accounts.CreateAsync("App",Input(),default);var tech=await accounts.CreateAsync("Tech",Input(),default);var admins=new AdministratorAccountRepository(cfg);var admin=await admins.CreateAsync(Input(),null,DateTime.UtcNow,default);
  var google=new ExternalLoginAdoNetRepository(cfg);var identity=new VerifiedGoogleIdentity("same-subject","same@example.test");
  await Assert.ThrowsAsync<ExternalLoginConflictException>(()=>google.FindOrCreateGoogleAsync(identity,default,"App"));
  await Assert.ThrowsAsync<ApplicationLoginException>(()=>google.FindOrCreateGoogleAsync(identity,default,"Tech"));
  await google.LinkGoogleAsync(app.Id,identity,default,"App");await google.LinkGoogleAsync(tech.Id,identity,default,"Tech");await google.LinkGoogleAsync(admin.UserId,identity,default,"Admin");
  Assert.Equal(app.Id,(await google.FindOrCreateGoogleAsync(identity,default,"App")).Id);Assert.Equal(tech.Id,(await google.FindOrCreateGoogleAsync(identity,default,"Tech")).Id);Assert.Equal(admin.UserId,(await google.FindOrCreateGoogleAsync(identity,default,"Admin")).Id);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>google.LinkGoogleAsync(app.Id,identity,default,"Tech"));
 }
 [IndependentMySqlFact]
 public async Task Failed_admin_audit_rolls_back_admin_account_and_bootstrap_marker()
 {
  var(cs,cfg)=await Reset();await using var c=new MySqlConnection(cs);await c.OpenAsync();
  await using(var fail=new MySqlCommand("CREATE TRIGGER fail_admin_audit BEFORE INSERT ON identity_administrator_account_creations FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test rollback'",c))await fail.ExecuteNonQueryAsync();
  var repo=new AdministratorAccountRepository(cfg);try{await Assert.ThrowsAsync<MySqlException>(()=>repo.CreateAsync(Input(),null,DateTime.UtcNow,default));}finally{await using var drop=new MySqlCommand("DROP TRIGGER fail_admin_audit",c);await drop.ExecuteNonQueryAsync();}
  var accounts=new ApplicationAccountRepository(cfg);Assert.Null(await accounts.FindAsync("Admin","same@example.test",default));var created=await repo.CreateAsync(Input(),null,DateTime.UtcNow,default);Assert.NotEqual(Guid.Empty,created.UserId);
 }
}
