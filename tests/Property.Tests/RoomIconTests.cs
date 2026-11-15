using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.Property.Api.Controllers;
using Zumaris.Property.Application.Features;
using Xunit;

namespace Property.Tests;

public sealed class RoomIconTests
{
    [Fact]
    public async Task Uses_live_catalog_icons_for_room_types_in_one_request_and_preserves_room_data()
    {
        var handler = new CatalogHandler(HttpStatusCode.OK,
            "[{\"id\":1,\"name\":\"Kitchen\",\"iconUrl\":\"/service-catalog/api/areas/icons/custom-kitchen.svg\"},{\"id\":5,\"iconUrl\":\"/service-catalog/api/areas/icons/living.png\"},{\"id\":12,\"iconUrl\":null}]");
        var icons = Create(handler);
        var rooms = new[] { Room("Kitchen"), Room("LivingRoom"), Room("Garden"), Room("Bathroom") };
        var result = await icons.Enrich(rooms, default);
        Assert.Equal("/service-catalog/api/areas/icons/custom-kitchen.svg", result[0].IconUrl);
        Assert.Equal("/service-catalog/api/areas/icons/living.png", result[1].IconUrl);
        Assert.Null(result[2].IconUrl);
        Assert.Null(result[3].IconUrl);
        Assert.Equal(rooms[0].RoomId, result[0].RoomId);
        Assert.Equal(rooms[0].Details, result[0].Details);
        Assert.Null(rooms[0].IconUrl);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("Bearer caller-token", handler.Authorization);
        Assert.Equal("https://catalog.example.test/api/areas", handler.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Optional_icons_do_not_break_room_results_when_catalog_is_unavailable(HttpStatusCode status)
    {
        var result = await Create(new CatalogHandler(status, "{}")).Enrich([Room("Kitchen")], default);
        Assert.Single(result);
        Assert.Null(result[0].IconUrl);
    }

    [Fact]
    public async Task Empty_page_does_not_contact_catalog()
    {
        var handler = new CatalogHandler(HttpStatusCode.OK, "[]");
        Assert.Empty(await Create(handler).Enrich([], default));
        Assert.Equal(0, handler.Calls);
    }

    private static PropertyRoom Room(string type) => new(Guid.NewGuid(), Guid.NewGuid(),
        new RoomInput("User-selected room name", type), DateTime.UtcNow, null, false);

    private static RoomIcons Create(CatalogHandler handler)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer caller-token";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ServiceCatalog:BaseUrl"] = "https://catalog.example.test/" }).Build();
        var http = new PassportHttpClient(new Factory(handler), new HttpContextAccessor { HttpContext = context }, config);
        return new RoomIcons(http, NullLogger<RoomIcons>.Instance);
    }

    private sealed class Factory(CatalogHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CatalogHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            Url = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
