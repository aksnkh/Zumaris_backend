using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Identity.Application.Abstractions.Security;
namespace Identity.Application.Features.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ForgotPasswordRequest([Required, EmailAddress, StringLength(254)] string Email);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetPasswordRequest([Required, StringLength(128)] string Token, [Required, StringLength(1024, MinimumLength = 8)] string NewPassword, [Required, StringLength(1024)] string ConfirmPassword);
public sealed class PasswordRecoverySettings
{
    public bool Enabled { get; set; }
    public int LifetimeMinutes { get; set; } = 15;
    public int CooldownSeconds { get; set; } = 60;
    public string AppResetUrl { get; set; } = "https://app.zumaris.com/reset-password";
    public string TechResetUrl { get; set; } = "https://tech.zumaris.com/reset-password";
    public string AdminResetUrl { get; set; } = "https://admin.zumaris.com/reset-password";
    public string ResetUrl(string application) => application switch { "App" => AppResetUrl, "Tech" => TechResetUrl, "Admin" => AdminResetUrl, _ => throw new RecoveryException(400, "Invalid application.") };
    public void Validate()
    {
        if (LifetimeMinutes is < 5 or > 30 || CooldownSeconds is < 60 or > 900) throw new InvalidOperationException("Password recovery lifetime must be 5–30 minutes and cooldown 60–900 seconds.");
        foreach (var app in new[] { "App", "Tech", "Admin" }) { if (!Uri.TryCreate(ResetUrl(app), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidOperationException("Recovery page URLs must use HTTPS or loopback HTTP with no credentials, query or fragment."); }
    }
}
public sealed class RecoveryException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record RecoveryEmail(Guid DeliveryId, string Application, string Recipient, string ResetUrl, DateTime ExpiresAtUtc);
public sealed record RecoveryIssue(Guid Id, string Application, string Email, string TokenHash, string ProtectedPayload, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, int CooldownSeconds);
public sealed record RecoveryDelivery(Guid Id, Guid LeaseId, string ProtectedPayload, int Attempts);
public interface IRecoveryProtector { string Protect(string plain); string Unprotect(string encrypted); }
public interface IPasswordRecoveryRepository
{
    Task<bool> Issue(RecoveryIssue issue, CancellationToken ct);
    Task<bool> Consume(string application, string tokenHash, string passwordHash, DateTime now, CancellationToken ct);
    Task<RecoveryDelivery?> Claim(DateTime now, CancellationToken ct);
    Task Complete(RecoveryDelivery delivery, bool sent, DateTime now, CancellationToken ct);
}
public interface IRecoveryEmailClient { Task Send(RecoveryEmail email, CancellationToken ct); }
public sealed class PasswordRecoveryService(IPasswordRecoveryRepository repository, IRecoveryProtector protector, IPasswordHasher passwords, PasswordRecoverySettings settings, TimeProvider clock)
{
    public const string AcceptedMessage = "If an eligible account exists, password reset instructions will be sent to its email address.";
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private void Available() { if (!settings.Enabled) throw new RecoveryException(503, "Password recovery is not configured."); }
    public async Task Forgot(string application, ForgotPasswordRequest input, CancellationToken ct)
    {
        Available(); var address = input.Email?.Trim().ToLowerInvariant() ?? "";
        if (address.Length > 254 || !new EmailAddressAttribute().IsValid(address)) throw new RecoveryException(400, "Enter a valid email address.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow().UtcDateTime; var id = Guid.NewGuid(); var expires = now.AddMinutes(settings.LifetimeMinutes);
        var link = new UriBuilder(settings.ResetUrl(application)) { Fragment = "token=" + Uri.EscapeDataString(token) }.Uri.AbsoluteUri;
        var encrypted = protector.Protect(JsonSerializer.Serialize(new RecoveryEmail(id, application, address, link, expires)));
        // Same public response for missing, inactive, deleted, Google-only and throttled accounts.
        await repository.Issue(new(id, application, address, TokenHash(token), encrypted, now, expires, settings.CooldownSeconds), ct);
    }
    public async Task Reset(string application, ResetPasswordRequest input, CancellationToken ct)
    {
        Available(); if (string.IsNullOrWhiteSpace(input.NewPassword) || input.NewPassword.Length is < 8 or > 1024 || input.NewPassword != input.ConfirmPassword) throw new RecoveryException(400, "Provide matching passwords of 8 to 1024 characters.");
        if (input.Token is null || input.Token.Length != 43 || input.Token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw InvalidLink();
        settings.ResetUrl(application);
        if (!await repository.Consume(application, TokenHash(input.Token), passwords.Hash(input.NewPassword), clock.GetUtcNow().UtcDateTime, ct)) throw InvalidLink();
    }
    private static RecoveryException InvalidLink() => new(400, "The password reset link is invalid or expired.");
}
