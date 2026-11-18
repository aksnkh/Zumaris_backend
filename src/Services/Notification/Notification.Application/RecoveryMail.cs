namespace Zumaris.Notification.Application;
public sealed record RecoveryMail(Guid DeliveryId, string Application, string Recipient, string ResetUrl, DateTime ExpiresAtUtc);
public interface IRecoveryMailSender { Task Send(RecoveryMail mail, CancellationToken ct); }
public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Zumaris";
    public bool EnableTls { get; set; } = true;
    public bool DevelopmentLoopback { get; set; }
}
