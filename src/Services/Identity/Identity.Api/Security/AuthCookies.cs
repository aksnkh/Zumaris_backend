namespace Identity.Api.Security;

internal static class AuthCookies
{
    internal static string NameFor(string application) => "zumaris_refresh_token_" + application.ToLowerInvariant();
}
