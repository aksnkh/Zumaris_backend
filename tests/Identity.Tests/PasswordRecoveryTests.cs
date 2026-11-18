using System.Text.Json;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Xunit;
namespace Identity.Tests;

public sealed class PasswordRecoveryTests
{
    private readonly MemoryRepository repository = new();
    private readonly MemoryProtection protection = new();
    private readonly FakePasswords passwords = new();
    private readonly PasswordRecoverySettings settings = new() { Enabled = true };
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private PasswordRecoveryService Service => new(repository, protection, passwords, settings, new Clock());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    [Theory]
    [InlineData("App", "app.zumaris.com")]
    [InlineData("Tech", "tech.zumaris.com")]
    [InlineData("Admin", "admin.zumaris.com")]
    public async Task Each_application_uses_its_own_link_and_stores_only_hash_and_protected_delivery(string application, string host)
    {
        await Service.Forgot(application, new("Person@Example.test"), default);
        var issue = Assert.Single(repository.Issues);
        var mail = JsonSerializer.Deserialize<RecoveryEmail>(protection.LastPlain!)!;
        var url = new Uri(mail.ResetUrl);
        Assert.Equal(application, issue.Application);
        Assert.Equal("person@example.test", issue.Email);
        Assert.Equal(host, url.Host);
        Assert.Empty(url.Query);
        Assert.StartsWith("#token=", url.Fragment);
        var raw = url.Fragment[7..];
        Assert.Equal(43, raw.Length);
        Assert.Equal(PasswordRecoveryService.TokenHash(raw), issue.TokenHash);
        Assert.DoesNotContain(raw, issue.ProtectedPayload);
        Assert.Equal(Now.UtcDateTime.AddMinutes(15), issue.ExpiresAtUtc);
        Assert.Equal(application, mail.Application);
    }

    [Fact]
    public async Task Missing_and_throttled_accounts_do_not_change_the_public_service_outcome()
    {
        repository.IssueResult = false;
        await Service.Forgot("App", new("unknown@example.test"), default);
        Assert.Single(repository.Issues);
    }

    [Fact]
    public async Task Reset_uses_hash_and_never_accepts_mismatched_passwords_or_malformed_token()
    {
        var raw = new string('a', 43);
        await Service.Reset("Admin", new(raw, "new-password", "new-password"), default);
        Assert.Equal(("Admin", PasswordRecoveryService.TokenHash(raw), "hashed:new-password"), repository.Consumed);
        var mismatch = await Assert.ThrowsAsync<RecoveryException>(() => Service.Reset("Admin", new(raw, "new-password", "different"), default));
        Assert.Equal(400, mismatch.Status);
        await Assert.ThrowsAsync<RecoveryException>(() => Service.Reset("Admin", new("invalid", "new-password", "new-password"), default));
    }

    [Fact]
    public async Task Invalid_expired_or_reused_token_has_the_same_generic_failure()
    {
        repository.ConsumeResult = false;
        var error = await Assert.ThrowsAsync<RecoveryException>(() => Service.Reset("Tech", new(new string('x', 43), "new-password", "new-password"), default));
        Assert.Equal(400, error.Status);
        Assert.Equal("The password reset link is invalid or expired.", error.Message);
    }

    [Fact]
    public async Task Disabled_feature_fails_equally_without_account_lookup()
    {
        settings.Enabled = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<RecoveryException>(() => Service.Forgot("App", new("user@example.test"), default))).Status);
        Assert.Empty(repository.Issues);
    }

    private sealed class MemoryProtection : IRecoveryProtector
    {
        public string? LastPlain;
        public string Protect(string plain) { LastPlain = plain; return "encrypted-payload"; }
        public string Unprotect(string encrypted) => LastPlain!;
    }
    private sealed class FakePasswords : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }
    private sealed class MemoryRepository : IPasswordRecoveryRepository
    {
        public List<RecoveryIssue> Issues = [];
        public bool IssueResult = true, ConsumeResult = true;
        public (string, string, string)? Consumed;
        public Task<bool> Issue(RecoveryIssue issue, CancellationToken ct) { Issues.Add(issue); return Task.FromResult(IssueResult); }
        public Task<bool> Consume(string app, string tokenHash, string passwordHash, DateTime now, CancellationToken ct) { Consumed = (app, tokenHash, passwordHash); return Task.FromResult(ConsumeResult); }
        public Task<RecoveryDelivery?> Claim(DateTime now, CancellationToken ct) => Task.FromResult<RecoveryDelivery?>(null);
        public Task Complete(RecoveryDelivery delivery, bool sent, DateTime now, CancellationToken ct) => Task.CompletedTask;
    }
}
