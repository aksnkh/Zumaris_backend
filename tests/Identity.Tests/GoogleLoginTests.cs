using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Features.Auth;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Tests;

public sealed class GoogleLoginTests
{
    [Fact]
    public async Task Swagger_exposes_google_and_bff_routes_with_required_key_header()
    {
        using var f = new AuthFactory(); using var c = f.NewClient();
        using var response = await c.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/auth/google", "/api/auth/google/link", "/api/auth/bff/refresh", "/api/auth/bff/logout" })
        {
            var operation = paths.GetProperty(path).GetProperty("post");
            var header = Assert.Single(operation.GetProperty("parameters").EnumerateArray(),
                p => p.GetProperty("name").GetString() == "X-Zumaris-Bff-Key");
            Assert.Equal("header", header.GetProperty("in").GetString());
            Assert.True(header.GetProperty("required").GetBoolean());
        }
    }

    private static HttpClient Client(AuthFactory f)
    {
        var client = f.NewClient(); client.DefaultRequestHeaders.Add("X-Zumaris-Bff-Key", AuthFactory.BffKey); return client;
    }
    private static Task<HttpResponseMessage> Google(HttpClient client) => client.PostAsJsonAsync("/api/auth/google", new { idToken = "test-google-id-token" });
    private static async Task<(Guid UserId, string Access, string Refresh)> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = json.RootElement.GetProperty("payload");
        return (payload.GetProperty("userId").GetGuid(), payload.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    [Fact]
    public async Task Google_creates_passwordless_user_and_reuses_stable_subject_with_same_internal_id()
    {
        using var f = new AuthFactory(); using var c = Client(f);
        using var first = await Google(c); var one = await Read(first);
        Assert.Equal("no-store", first.Headers.CacheControl!.ToString()); Assert.False(first.Headers.Contains("Set-Cookie"));
        Assert.Null(Assert.Single(f.Users.GoogleUsers).PasswordHash);
        f.Google.Identity = f.Google.Identity with { Email = "changed-google-email@example.test" };
        var two = await Read(await Google(c));
        Assert.Equal(one.UserId, two.UserId); Assert.Single(f.Users.GoogleUsers); Assert.Equal(2, f.Sessions.Items.Count);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", one.Access);
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Bff_refresh_rotates_and_logout_revokes_the_same_identity_session()
    {
        using var f = new AuthFactory(); using var c = Client(f); var login = await Read(await Google(c));
        var next = await Read(await c.PostAsJsonAsync("/api/auth/bff/refresh", new { refreshToken = login.Refresh }));
        Assert.Equal(login.UserId, next.UserId); Assert.NotEqual(login.Refresh, next.Refresh);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/bff/logout", new { refreshToken = next.Refresh })).StatusCode);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Passwordless_account_never_creates_a_password_session(string? hash)
    {
        using var f = new AuthFactory(); using var c = Client(f);
        f.Users.User.PasswordHash = hash;
        var response = await c.PostAsJsonAsync("/api/auth/login",
            new { userName = f.Users.User.UserName, password = "Password-for-tests-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(f.Sessions.Items);
    }

    [Fact]
    public async Task Different_google_subject_with_same_email_cannot_take_over_passwordless_account()
    {
        using var f = new AuthFactory(); using var c = Client(f);
        var original = await Read(await Google(c));
        f.Google.Identity = f.Google.Identity with { Subject = "different-google-subject" };
        Assert.Equal(HttpStatusCode.Conflict, (await Google(c)).StatusCode);
        Assert.Equal(original.UserId, Assert.Single(f.Users.GoogleUsers).Id);
        Assert.Null(Assert.Single(f.Users.GoogleUsers).PasswordHash);
        Assert.Single(f.Sessions.Items);
    }

    [Fact]
    public async Task Reuse_of_bff_refresh_revokes_session_and_rejects_new_token()
    {
        using var f = new AuthFactory(); using var c = Client(f); var login = await Read(await Google(c));
        var rotated = await Read(await c.PostAsJsonAsync("/api/auth/bff/refresh", new { refreshToken = login.Refresh }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/bff/refresh", new { refreshToken = login.Refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/bff/refresh", new { refreshToken = rotated.Refresh })).StatusCode);
    }

    [Fact]
    public async Task Existing_email_is_not_automatically_linked()
    {
        using var f = new AuthFactory(); using var c = Client(f);
        f.Google.Identity = f.Google.Identity with { Email = f.Users.User.Email.ToUpperInvariant() };
        Assert.Equal(HttpStatusCode.Conflict, (await Google(c)).StatusCode);
        Assert.Empty(f.Users.GoogleUsers); Assert.Empty(f.Sessions.Items);
    }

    [Fact]
    public async Task Explicit_link_requires_authenticated_owner_and_preserves_existing_password()
    {
        using var f = new AuthFactory(); using var c = Client(f);
        f.Google.Identity = f.Google.Identity with { Email = f.Users.User.Email };
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/google/link", new { idToken = "test-token" })).StatusCode);
        using var login = await c.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = "Password-for-tests-1" });
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("payload").GetProperty("accessToken").GetString());
        var hash = f.Users.User.PasswordHash;
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/google/link", new { idToken = "test-token" })).StatusCode);
        Assert.Equal(f.Users.User.Id, (await Read(await Google(c))).UserId);
        Assert.Equal(hash, f.Users.User.PasswordHash); Assert.Empty(f.Users.GoogleUsers);
    }

    [Fact]
    public async Task Google_only_user_cannot_login_with_password_and_disabled_account_cannot_login_with_google()
    {
        using var f = new AuthFactory(); using var c = Client(f); await Read(await Google(c));
        var user = Assert.Single(f.Users.GoogleUsers);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { userName = user.UserName, password = "anything" })).StatusCode);
        user.IsActive = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google(c)).StatusCode);
        Assert.Single(f.Sessions.Items);
    }

    [Fact]
    public async Task Missing_or_wrong_bff_key_never_calls_google_or_creates_session()
    {
        using var f = new AuthFactory(); using var c = f.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google(c)).StatusCode);
        c.DefaultRequestHeaders.Add("X-Zumaris-Bff-Key", "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google(c)).StatusCode);
        Assert.Equal(0, f.Google.Calls); Assert.Empty(f.Sessions.Items);
    }

    [Fact]
    public async Task Invalid_google_token_and_provider_outage_fail_without_creating_user()
    {
        using var f = new AuthFactory(); using var c = Client(f); f.Google.Reject = true;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Google(c)).StatusCode);
        f.Google.Reject = false; f.Google.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Google(c)).StatusCode);
        Assert.Empty(f.Users.GoogleUsers); Assert.Empty(f.Sessions.Items);
    }

    [Fact]
    public async Task Official_verifier_is_disabled_without_client_id_and_rejects_malformed_token()
    {
        var missing = new GoogleIdentityValidator(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<GoogleLoginUnavailableException>(() => missing.ValidateAsync("bad-token", default));
        var configured = new GoogleIdentityValidator(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Google:ClientIds:0"] = "test-only.apps.googleusercontent.com" }).Build());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => configured.ValidateAsync("not-a-jwt", default));
    }
}
