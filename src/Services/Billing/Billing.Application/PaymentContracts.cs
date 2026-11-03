using System.ComponentModel.DataAnnotations;
namespace Zumaris.Billing.Application;
public sealed record CreatePaymentRequest(Guid RequestId);
public sealed record CompletedWork(Guid RequestId,Guid CustomerId,Guid ProviderId,string Status,Guid QuoteId,decimal Amount,string CurrencyCode);
public sealed record PaymentIntent(Guid Id,Guid RequestId,Guid QuoteId,decimal Amount,string CurrencyCode,string Gateway,string Status,DateTime CreatedAtUtc)
{
 public bool CanPay=>false;
 public string? CheckoutUrl=>null;
}
public interface IPaymentRepository
{
 Task<PaymentIntent> CreateAsync(Guid customer,CompletedWork work,CancellationToken ct);
 Task<PaymentIntent?> GetAsync(Guid customer,Guid id,CancellationToken ct);
}
public interface ICompletedWorkClient {Task<CompletedWork> GetAsync(Guid customer,Guid id,string bearer,CancellationToken ct);}
public interface IIdentitySessionValidator {Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct);}
public sealed class BillingException(int status,string message):Exception(message){public int Status{get;}=status;}
public sealed class PaymentService(IPaymentRepository repository,ICompletedWorkClient completed)
{
 public async Task<PaymentIntent> CreateAsync(Guid actor,CreatePaymentRequest input,string bearer,CancellationToken ct)
 {
  if(input.RequestId==Guid.Empty)throw new BillingException(400,"A nonempty requestId is required.");
  var work=await completed.GetAsync(actor,input.RequestId,bearer,ct);
  if(work.CustomerId!=actor||work.RequestId!=input.RequestId||work.Status!="Completed"||work.Amount<=0||work.CurrencyCode!="GBP"||work.QuoteId==Guid.Empty)
   throw new BillingException(409,"Only completed jobs with an accepted fixed-price quote can be paid.");
  return await repository.CreateAsync(actor,work,ct);
 }
}
