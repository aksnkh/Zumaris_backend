using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zumaris.ServiceRequests.Application;
using Xunit;

namespace ServiceRequest.Tests;

public sealed class HistoryTests
{
    [Theory]
    [InlineData("/api/service-history")]
    [InlineData("/api/service-history/00000000-0000-0000-0000-000000000001")]
    public async Task Requires_authenticated_active_identity(string path)
    {
        using var f = new RequestFactory(); using var anonymous = f.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        f.Validator.Active = false; using var revoked = f.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync(path)).StatusCode);
        Assert.Equal(0, f.HistoryRepository.Calls);
    }
    [Fact]
    public async Task List_uses_token_owner_defaults_and_returns_empty_pagination()
    {
        using var f = new RequestFactory(); using var c = f.Client();
        using var r = await c.GetAsync("/api/service-history?userId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        var page = await r.Content.ReadFromJsonAsync<PagedResult<ServiceHistoryDto>>();
        Assert.Empty(page!.Items); Assert.Equal(0, page.TotalCount); Assert.Equal(0, page.TotalPages);
        Assert.Equal(1, page.PageNumber); Assert.Equal(20, page.PageSize);
        Assert.Equal(f.UserId, f.HistoryRepository.LastUser);
        Assert.Null(f.HistoryRepository.LastFilter!.Status);
    }
    [Theory]
    [InlineData("All", null)] [InlineData("", null)]
    [InlineData("completed", "Completed")] [InlineData("Cancelled", "Cancelled")]
    [InlineData(" PAID ", "Paid")]
    public async Task Normalizes_supported_filters_before_repository(string input, string? expected)
    {
        using var f = new RequestFactory(); using var c = f.Client();
        var r = await c.GetAsync("/api/service-history?status=" + Uri.EscapeDataString(input) + "&pageNumber=2&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(new HistoryFilterRequest(expected, 2, 5), f.HistoryRepository.LastFilter);
    }
    [Theory]
    [InlineData("status=InProgress")] [InlineData("status=Paid%27%20OR%201=1")]
    [InlineData("pageNumber=0")] [InlineData("pageNumber=-1")]
    [InlineData("pageSize=0")] [InlineData("pageSize=101")]
    [InlineData("pageNumber=2147483648")] [InlineData("pageSize=text")]
    public async Task Invalid_filters_and_pagination_never_reach_repository(string query)
    {
        using var f = new RequestFactory(); using var c = f.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/service-history?" + query)).StatusCode);
        Assert.Equal(0, f.HistoryRepository.Calls);
    }
    [Fact]
    public async Task Paid_filter_preserves_service_status_and_nullable_unknown_values()
    {
        using var f = new RequestFactory(); using var c = f.Client();
        var id = Guid.NewGuid();
        f.HistoryRepository.Item = new(id,id,"SR-"+id.ToString("N"),f.UserId,Guid.NewGuid(),null,1,null,
            null,null,"A request with no category snapshot yet.",null,null,null,null,null,null,"Cancelled","Paid",null,null,DateTime.UtcNow,null);
        var page = await c.GetFromJsonAsync<PagedResult<ServiceHistoryDto>>("/api/service-history?status=Paid");
        var item = Assert.Single(page!.Items);
        Assert.Equal("Cancelled", item.Status); Assert.Equal("Paid", item.PaymentStatus);
        Assert.Null(item.TotalAmount); Assert.Null(item.CurrencyCode); Assert.Null(item.ServiceDate);
        var excluded = await c.GetFromJsonAsync<PagedResult<ServiceHistoryDto>>("/api/service-history?status=Completed");
        Assert.Empty(excluded!.Items);
    }
    [Fact]
    public async Task Details_only_return_owners_request_and_share_id_with_request()
    {
        using var f = new RequestFactory(); using var owner = f.Client(); var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        f.Icons.Url = "/service-catalog/api/categories/icons/1.svg";
        f.HistoryRepository.Item = new(id,id,"SR-"+id.ToString("N"),f.UserId,Guid.NewGuid(),"Address",1,"Plumbing",
            "Tap repair","Kitchen","A leaking tap requires repair.",null,null,null,null,now,"Changed plans","Cancelled","Unpaid",null,null,now,now);
        using var r = await owner.GetAsync("/api/service-history/" + id);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Equal("no-store",r.Headers.CacheControl!.ToString());
        var item = await r.Content.ReadFromJsonAsync<ServiceHistoryDetailDto>();
        Assert.Equal("/service-catalog/api/categories/icons/1.svg",item!.CategoryIconUrl);
        var page = await owner.GetFromJsonAsync<PagedResult<ServiceHistoryDto>>("/api/service-history");
        Assert.Equal(f.Icons.Url,Assert.Single(page!.Items).CategoryIconUrl);
        Assert.Equal(f.Icons.Url,page.Items[0].CategoryIcon);
        Assert.Equal(id,item!.HistoryId); Assert.Equal(id,item.RequestId); Assert.Equal("Changed plans",item.CancellationReason);
        using var other = f.Client(userId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api/service-history/"+id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync("/api/service-history/"+Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Swagger_lists_both_history_routes()
    {
        using var f = new RequestFactory(); using var c = f.Client(false);
        using var json = JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/service-history").TryGetProperty("get",out _));
        Assert.True(paths.GetProperty("/api/service-history/{id}").TryGetProperty("get",out _));
    }
}

// Contract fixture: SQL ownership predicates and database execution need MySQL validation separately.
public sealed class HistoryRepositoryFake : IServiceHistoryRepository
{
    public Guid LastUser { get; private set; }
    public HistoryFilterRequest? LastFilter { get; private set; }
    public int Calls { get; private set; }
    public ServiceHistoryDetailDto? Item { get; set; }
    public Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid user, HistoryFilterRequest request, CancellationToken ct)
    {
        Calls++; LastUser=user; LastFilter=request;
        var list = new List<ServiceHistoryDto>();
        if (Item is { } x && x.UserId==user && (request.Status is null ||
            (request.Status=="Paid" ? x.PaymentStatus=="Paid" : x.Status==request.Status)))
            list.Add(new(x.RequestId,x.RequestNumber,x.ServiceName,x.CategoryName,null,x.ServiceArea,x.TechnicianName,
                x.ServiceDate,x.Status,x.PaymentStatus,x.TotalAmount,x.CurrencyCode,x.CreatedAt) { CategoryId = x.CategoryId });
        return Task.FromResult(new PagedResult<ServiceHistoryDto>(list,request.PageNumber,request.PageSize,list.Count));
    }
    public Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid user, Guid id, CancellationToken ct)
    {
        Calls++; LastUser=user;
        return Task.FromResult(Item is { } x && x.UserId==user && x.RequestId==id ? Item : null);
    }
}
