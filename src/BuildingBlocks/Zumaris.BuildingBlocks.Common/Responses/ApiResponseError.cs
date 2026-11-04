using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json.Serialization;
namespace Zumaris.BuildingBlocks.Common.Responses;

// Opt-in error envelope for new endpoints. Existing endpoint formats are unchanged.
public sealed class ApiResponseError : Response<object?>
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,string[]>? Errors {get;init;}
    public string TraceId {get;init;}="";
    public static ApiResponseError Create(int status,string message,string traceId,IReadOnlyDictionary<string,string[]>? errors=null)=>new()
    {StatusCode=status,Title=status>=500?"Service Error":status==409?"Conflict":status==401?"Unauthorized":"Request Failed",Message=message,
     MessageLevel=MessageLevel.Cif,Payload=null,Errors=errors,TraceId=traceId};
}

[AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)]
public sealed class ResponseErrorsAttribute : Attribute,IAlwaysRunResultFilter,IOrderedFilter
{
    public int Order=>int.MaxValue;
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if(context.Result is ObjectResult {Value:ProblemDetails problem} result)
        {
            var status=result.StatusCode??problem.Status??500;
            if(status<400)return;
            var errors=(problem as ValidationProblemDetails)?.Errors;
            var messages=errors?.Values.SelectMany(x=>x).Distinct().ToArray();
            var message=messages?.Length>0?string.Join(" ",messages):problem.Detail??problem.Title??"The request could not be processed.";
            result.Value=ApiResponseError.Create(status,message,context.HttpContext.TraceIdentifier,errors is null?null:new Dictionary<string,string[]>(errors));
            result.DeclaredType=typeof(ApiResponseError);result.StatusCode=status;result.ContentTypes.Clear();result.ContentTypes.Add("application/json");
        }
        if(context.Result is ObjectResult response && response.Value is not null && IsResponse(response.Value.GetType()))
        {
            // Scope the enum format to opted-in endpoints; legacy responses are unchanged.
            var options=new JsonSerializerOptions(context.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions);
            options.Converters.Insert(0,new JsonStringEnumConverter<MessageLevel>());
            response.Formatters.Insert(0,new SystemTextJsonOutputFormatter(options));
        }
    }
    private static bool IsResponse(Type? type)
    {
        while(type is not null){if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Response<>))return true;type=type.BaseType;}
        return false;
    }
    public void OnResultExecuted(ResultExecutedContext context){}
}
