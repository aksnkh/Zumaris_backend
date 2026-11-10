using Identity.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Infrastructure.Persistence;
public sealed class AuthAdoNetRepository(IConfiguration configuration):IAuthRepository
{
 private readonly ApplicationAccountRepository accounts=new(configuration);
 public Task<User?> GetUserByUsernameAsync(string username,CancellationToken ct=default)=>accounts.FindAsync("App",username,ct);
 public async Task<bool> RegisterAsync(string username,string passwordHash,string email)
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
  await using var cmd=new MySqlCommand("INSERT INTO app_accounts(id,user_name,email,password_hash,created_at,updated_at)VALUES(@id,@name,@email,@hash,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))",c,tx);
  cmd.Parameters.AddWithValue("@id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("@name",username.Trim());cmd.Parameters.AddWithValue("@email",email.Trim().ToLowerInvariant());cmd.Parameters.AddWithValue("@hash",passwordHash);
  try{await cmd.ExecuteNonQueryAsync();await tx.CommitAsync();return true;}catch(MySqlException ex)when(ex.Number==1062){throw new InvalidOperationException("Username already exists or email is already registered in App.");}
 }
}
