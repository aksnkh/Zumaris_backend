using System.Net.Http.Json;
using Zumaris.ServiceRequests.Application;
using Xunit;
namespace ServiceRequest.Tests;

public class HistoryPreferredScheduleTests
{
    [Fact]
    public async Task List_and_details_return_selected_schedule_separately_from_actual_service_date()
    {
        using var f=new RequestFactory();using var c=f.Client();var id=Guid.NewGuid();
        var actual=new DateTime(2030,7,2,9,0,0,DateTimeKind.Utc);
        f.HistoryRepository.Item=new(id,id,"SR-test",f.UserId,Guid.NewGuid(),null,1,"Plumbing",null,"Bathroom","A leaking tap requires repair.",
            null,null,actual,actual,null,null,"Completed","Unpaid",null,null,DateTime.UtcNow,null) {
                PreferredDate=new DateOnly(2030,7,1),PreferredStartTime=new TimeOnly(10,0),PreferredEndTime=new TimeOnly(12,0),TimeZoneId="Europe/London"
            };
        var detail=await c.GetFromJsonAsync<ServiceHistoryDetailDto>("/api/service-history/"+id);
        Assert.Equal(new DateOnly(2030,7,1),detail!.PreferredDate);Assert.Equal(new TimeOnly(10,0),detail.PreferredStartTime);
        Assert.Equal(new TimeOnly(12,0),detail.PreferredEndTime);Assert.Equal("Europe/London",detail.TimeZoneId);Assert.Equal(actual,detail.ServiceDate);
        var page=await c.GetFromJsonAsync<PagedResult<ServiceHistoryDto>>("/api/service-history");var item=Assert.Single(page!.Items);
        Assert.Equal(detail.PreferredDate,item.PreferredDate);Assert.Equal(detail.PreferredStartTime,item.PreferredStartTime);
        Assert.Equal(detail.PreferredEndTime,item.PreferredEndTime);Assert.Equal(detail.TimeZoneId,item.TimeZoneId);Assert.Equal(actual,item.ServiceDate);
        Assert.Null(item.TotalAmount);Assert.Equal("Unpaid",item.PaymentStatus);
    }

    [Fact]
    public async Task Unscheduled_request_does_not_invent_a_preferred_date_from_created_at()
    {
        using var f=new RequestFactory();using var c=f.Client();var id=Guid.NewGuid();
        f.HistoryRepository.Item=new(id,id,"SR-test",f.UserId,Guid.NewGuid(),null,1,"Plumbing",null,null,"A leaking tap requires repair.",
            null,null,null,null,null,null,"Pending","Unpaid",null,null,DateTime.UtcNow,null);
        var detail=await c.GetFromJsonAsync<ServiceHistoryDetailDto>("/api/service-history/"+id);
        Assert.Null(detail!.PreferredDate);Assert.Null(detail.PreferredStartTime);Assert.Null(detail.PreferredEndTime);Assert.Null(detail.ServiceDate);
    }
}
