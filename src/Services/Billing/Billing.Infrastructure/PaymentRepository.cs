using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Billing.Application;
namespace Zumaris.Billing.Infrastructure;
public sealed class PaymentRepository(IConfiguration configuration):IPaymentRepository
{
 private async Task<MySqlConnection> Open(CancellationToken ct){var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
 private static PaymentIntent Read(MySqlDataReader r)=>new(r.GetGuid(r.GetOrdinal("id")),r.GetGuid(r.GetOrdinal("request_id")),r.GetGuid(r.GetOrdinal("quote_id")),r.GetDecimal("amount"),r.GetString("currency_code"),r.GetString("gateway"),r.GetString("status"),DateTime.SpecifyKind(r.GetDateTime("created_at"),DateTimeKind.Utc));
 public async Task<PaymentIntent> CreateAsync(Guid customer,CompletedWork work,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);
  await using(var cmd=new MySqlCommand("""
   INSERT INTO payment_intents(id,request_id,quote_id,customer_id,amount,currency_code,gateway,status,created_at)
   VALUES(@id,@request,@quote,@customer,@amount,@currency,'PayPal','AwaitingGateway',@now)
   ON DUPLICATE KEY UPDATE request_id=payment_intents.request_id
   """,c,tx))
  {
   cmd.Parameters.AddWithValue("@id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("@request",work.RequestId.ToString());cmd.Parameters.AddWithValue("@quote",work.QuoteId.ToString());cmd.Parameters.AddWithValue("@customer",customer.ToString());cmd.Parameters.AddWithValue("@amount",work.Amount);cmd.Parameters.AddWithValue("@currency",work.CurrencyCode);cmd.Parameters.AddWithValue("@now",DateTime.UtcNow);await cmd.ExecuteNonQueryAsync(ct);
  }
  await using var query=new MySqlCommand("SELECT * FROM payment_intents WHERE request_id=@id AND customer_id=@customer FOR UPDATE",c,tx);query.Parameters.AddWithValue("@id",work.RequestId.ToString());query.Parameters.AddWithValue("@customer",customer.ToString());
  PaymentIntent item;await using(var reader=await query.ExecuteReaderAsync(ct)){if(!await reader.ReadAsync(ct))throw new BillingException(409,"The payment request conflicts with an existing record.");item=Read(reader);}
  if(item.QuoteId!=work.QuoteId||item.Amount!=work.Amount||item.CurrencyCode!=work.CurrencyCode)throw new BillingException(409,"The fixed-price payment details do not match.");
  await tx.CommitAsync(ct);return item;
 }
 public async Task<PaymentIntent?> GetAsync(Guid customer,Guid id,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var cmd=new MySqlCommand("SELECT * FROM payment_intents WHERE id=@id AND customer_id=@customer",c);cmd.Parameters.AddWithValue("@id",id.ToString());cmd.Parameters.AddWithValue("@customer",customer.ToString());await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?Read(r):null;
 }
}
