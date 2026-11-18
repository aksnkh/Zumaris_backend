using System.Text.Json;
using Identity.Application.Features.Auth;
namespace Identity.Api.Security;
public sealed class RecoveryEmailWorker(IServiceScopeFactory scopes, PasswordRecoverySettings settings, ILogger<RecoveryEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var repository = scope.ServiceProvider.GetRequiredService<IPasswordRecoveryRepository>();
                var delivery = await repository.Claim(DateTime.UtcNow, stoppingToken);
                if (delivery is not null)
                {
                    bool sent = false;
                    try { var plain = scope.ServiceProvider.GetRequiredService<IRecoveryProtector>().Unprotect(delivery.ProtectedPayload); var email = JsonSerializer.Deserialize<RecoveryEmail>(plain) ?? throw new InvalidOperationException(); if (email.ExpiresAtUtc > DateTime.UtcNow) { await scope.ServiceProvider.GetRequiredService<IRecoveryEmailClient>().Send(email, stoppingToken); sent = true; } }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { logger.LogWarning("Recovery delivery {DeliveryId} will be retried ({FailureType}).", delivery.Id, ex.GetType().Name); }
                    await repository.Complete(delivery, sent, DateTime.UtcNow, stoppingToken);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Recovery outbox is unavailable ({FailureType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
