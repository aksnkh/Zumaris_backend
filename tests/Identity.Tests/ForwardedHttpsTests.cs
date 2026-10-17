using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Tests;

public sealed class ForwardedHttpsTests
{
    [Theory]
    [InlineData("127.0.0.1", HttpStatusCode.Unauthorized)]
    [InlineData("::1", HttpStatusCode.Unauthorized)]
    [InlineData("203.0.113.4", HttpStatusCode.TemporaryRedirect)]
    public async Task Only_local_proxy_can_forward_https(string remoteIp, HttpStatusCode expected)
    {
        using var factory = new AuthFactory { EnvironmentName = "Production" };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
            services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(remoteIp)));
        }));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "198.51.100.20");
        request.Headers.Add("X-Zumaris-CSRF", "1");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Unauthorized)
            Assert.All(response.Headers.GetValues("Set-Cookie"), c => Assert.Contains("secure", c.ToLowerInvariant()));
    }

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = address;
                return continuation(context);
            });
            next(app);
        };
    }
}
