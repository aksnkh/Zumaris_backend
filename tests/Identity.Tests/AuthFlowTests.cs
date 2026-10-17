using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth.Models;
using Identity.Domain.Entities;
using Identity.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Xunit;

namespace Identity.Tests;

public sealed class AuthFlowTests
{
    private static async Task<(string Access, string Refresh, DateTime SessionExpiry)> Login(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
            { userName = "alice", password = "Password-for-tests-1" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = json.RootElement.GetProperty("payload");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => !c.Contains("expires=Thu, 01 Jan 1970"));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.Contains("path=/;", cookie.ToLowerInvariant());
        return (payload.GetProperty("accessToken").GetString()!, cookie.Split(';')[0].Split('=', 2)[1],
            payload.GetProperty("refreshTokenExpiresAtUtc").GetDateTime());
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", "zumaris_refresh_token=" + token);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Login_stores_only_hash_and_issues_session_bound_short_lived_jwt()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Access);
        Assert.Equal(factory.Users.User.Id.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == "sid");
        Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        Assert.DoesNotContain(login.Refresh, factory.Sessions.Tokens.Keys);
        Assert.All(factory.Sessions.Tokens.Keys, hash => Assert.Equal(64, hash.Length));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Rotation_changes_token_preserves_absolute_expiry_and_replay_revokes_family()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        using var rotated = await Refresh(client, login.Refresh);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        using var body = JsonDocument.Parse(await rotated.Content.ReadAsStringAsync());
        Assert.Equal(login.SessionExpiry, body.RootElement.GetProperty("payload").GetProperty("refreshTokenExpiresAtUtc").GetDateTime());
        var next = rotated.Headers.GetValues("Set-Cookie").Single(c => !c.Contains("expires=Thu, 01 Jan 1970")).Split(';')[0].Split('=', 2)[1];
        Assert.NotEqual(login.Refresh, next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, next)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Cookie_logout_needs_no_access_token_and_revokes_access_immediately()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", "zumaris_refresh_token=" + login.Refresh);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Refresh)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Revoke_all_invalidates_both_sessions()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var first = await Login(client); var second = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Access);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, first.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, second.Refresh)).StatusCode);
    }

    [Fact]
    public async Task Revoke_current_session_invalidates_access_and_refresh()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var sessionId = new JwtSecurityTokenHandler().ReadJwtToken(login.Access).Claims.Single(c => c.Type == "sid").Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/sessions/" + sessionId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sessions")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Refresh)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_with_same_token_revokes_session_on_reuse()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var responses = await Task.WhenAll(Refresh(client, login.Refresh), Refresh(client, login.Refresh));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.All(factory.Sessions.Items.Values, s => Assert.NotNull(s.RevokedAtUtc));
    }

    [Fact]
    public async Task Production_refresh_cookie_is_secure()
    {
        using var factory = new AuthFactory { EnvironmentName = "Production" };
        using var client = factory.NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
            { userName = "alice", password = "Password-for-tests-1" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(response.Headers.GetValues("Set-Cookie"), c => Assert.Contains("secure", c.ToLowerInvariant()));
    }

    [Fact]
    public async Task Empty_login_and_short_registration_password_are_rejected()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register",
            new { username = "bob", password = "short", email = "bob@example.test" })).StatusCode);
        Assert.Empty(factory.Sessions.Items);
    }

    [Fact]
    public async Task Authentication_requests_are_rate_limited()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Cannot_revoke_another_users_session()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var other = new AuthSession(Guid.NewGuid(), Guid.NewGuid(), "other", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null);
        await factory.Sessions.CreateAsync(other, new string('a', 64));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/sessions/" + other.Id)).StatusCode);
        Assert.True(await factory.Sessions.IsActiveAsync(other.Id, other.UserId));
        var list = await client.GetStringAsync("/api/auth/sessions");
        Assert.DoesNotContain(other.Id.ToString(), list);
    }

    [Fact]
    public async Task Disabled_user_cannot_refresh_and_session_is_revoked()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client); factory.Users.User.IsActive = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Refresh)).StatusCode);
        Assert.All(factory.Sessions.Items.Values, s => Assert.NotNull(s.RevokedAtUtc));
    }

    [Fact]
    public async Task Expired_session_cannot_refresh_or_use_access_token()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var session = factory.Sessions.Items.Values.Single();
        factory.Sessions.Items[session.Id] = session with { ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Refresh)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Csrf_header_is_required_for_cookie_mutations()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient(false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Bad_login_does_not_create_session_or_leak_details()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Sessions.Items);
        Assert.DoesNotContain("StackTrace", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Credentialed_cors_is_denied_for_unconfigured_origin()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", "https://untrusted.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "X-Zumaris-CSRF");
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("algorithm")]
    [InlineData("expired")]
    [InlineData("missing-session")]
    public async Task Invalid_access_tokens_are_rejected(string invalid)
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        var login = await Login(client);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(login.Access);
        var claims = original.Claims.Where(c => c.Type is not "exp" and not "nbf" and not "iat").ToList();
        if (invalid == "missing-session") claims.RemoveAll(c => c.Type == "sid");
        var key = invalid == "signature" ? new string('z', 64) : AuthFactory.TestKey;
        var jwt = new JwtSecurityToken(invalid == "issuer" ? "other" : "identity-tests",
            invalid == "audience" ? "other" : "identity-test-clients", claims,
            DateTime.UtcNow.AddMinutes(-30), invalid == "expired" ? DateTime.UtcNow.AddMinutes(-2) : DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                invalid == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Swagger_server_supports_direct_and_gateway_paths()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var server = json.RootElement.GetProperty("servers")[0].GetProperty("url").GetString();
        Assert.Equal("http://example.test/", new Uri(new Uri("http://example.test/swagger/v1/swagger.json"), server!).ToString());
        Assert.Equal("http://example.test/identity/", new Uri(new Uri("http://example.test/identity/swagger/v1/swagger.json"), server!).ToString());
    }

    [Fact]
    public async Task Session_validation_contract_is_204_only_for_active_authenticated_session()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        var login = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Access);
        using var active = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.NoContent, active.StatusCode);
        Assert.Equal("no-store", active.Headers.CacheControl?.ToString());
        await factory.Sessions.RevokeAllAsync(factory.Users.User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public void Jwt_settings_reject_weak_keys_and_unbounded_lifetimes()
    {
        var settings = new JwtSettings { Key = "short", Issuer = "issuer", Audience = "audience" };
        Assert.Throws<InvalidOperationException>(settings.Validate);
        settings.Key = new string('k', 64); settings.AccessTokenExpirationMinutes = 31;
        Assert.Throws<InvalidOperationException>(settings.Validate);
    }
}
