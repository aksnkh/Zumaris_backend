using System.Net;
using System.Net.Mail;
using System.Text;
using Zumaris.Notification.Application;
namespace Zumaris.Notification.Infrastructure;
public sealed class RecoverySmtpSender(SmtpSettings settings) : IRecoveryMailSender
{
    public async Task Send(RecoveryMail mail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is < 1 or > 65535 || !MailAddress.TryCreate(settings.FromAddress, out var from) || !MailAddress.TryCreate(mail.Recipient, out var recipient)) throw new InvalidOperationException("Email delivery is not configured.");
        if (!settings.EnableTls && !(settings.DevelopmentLoopback && IPAddress.TryParse(settings.Host, out var address) && IPAddress.IsLoopback(address))) throw new InvalidOperationException("SMTP requires TLS outside development loopback.");
        using var smtp = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableTls, UseDefaultCredentials = false, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrEmpty(settings.Username)) smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);
        using var message = new MailMessage(new MailAddress(from.Address, settings.FromName), recipient)
        {
            Subject = $"Reset your Zumaris {mail.Application} password",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
            Body = $"A password reset was requested for your Zumaris {mail.Application} account.\n\nOpen this link to choose a new password:\n{mail.ResetUrl}\n\nThe link expires at {mail.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC and can be used only once.\nIf you did not request this, you can ignore this email. Your password has not changed."
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await smtp.SendMailAsync(message, timeout.Token);
    }
}
