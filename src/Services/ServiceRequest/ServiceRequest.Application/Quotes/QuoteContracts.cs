namespace Zumaris.ServiceRequests.Application.Quotes;

public sealed record CreateQuoteRequest(decimal LabourAmount,decimal CalloutAmount,string CurrencyCode="GBP",
    string? ProviderNote=null,string? PriceSummary=null,DateTime? ExpiresAtUtc=null);
public sealed record ProviderQuoteIdentity(Guid ProviderId,Guid UserId,string DisplayName,string? PhotoUrl,
    decimal? Rating,int? CompletedJobs,bool IsVerified,int CategoryId);
public sealed record QuoteDisplay(string? CategoryName,string? AreaName,string? CategoryIconUrl);
public sealed record QuoteContext(Guid RequestId,Guid UserId,Guid PropertyId,int CategoryId,int AreaTypeId,
    string RequestNumber,string Description,string Status,int RequestVersion,string? CategoryName,string? AreaName,
    string? PropertyAddress,DateOnly? PreferredDate,TimeOnly? PreferredStartTime,TimeOnly? PreferredEndTime,string TimeZoneId);
public sealed record ProviderQuoteContext(Guid RequestId,string RequestNumber,int CategoryId,int AreaTypeId,string RequestDescription,
    string? CategoryName,string? AreaName,DateOnly? PreferredDate,TimeOnly? PreferredStartTime,TimeOnly? PreferredEndTime,string TimeZoneId);
public sealed record QuoteDto(Guid Id,Guid RequestId,string RequestNumber,Guid PropertyId,string? PropertyAddress,
    int CategoryId,int AreaTypeId,string? CategoryName,string? AreaName,string? CategoryIconUrl,string RequestDescription,
    DateOnly? PreferredDate,TimeOnly? PreferredStartTime,TimeOnly? PreferredEndTime,string TimeZoneId,
    Guid ProviderId,string ProviderName,string? ProviderPhotoUrl,decimal? ProviderRating,int? ProviderCompletedJobs,bool ProviderVerified,
    string Status,decimal LabourAmount,decimal CalloutAmount,decimal TotalAmount,string CurrencyCode,
    string? ProviderNote,string PriceSummary,DateTime CreatedAtUtc,DateTime? ExpiresAtUtc,DateTime? DecidedAtUtc,string? DecisionReason);
public sealed record QuotePage(IReadOnlyList<QuoteDto> Items,int PageNumber,int PageSize,long TotalCount)
{
    public long TotalPages=>TotalCount/PageSize+(TotalCount%PageSize==0?0:1);
}
public interface IProviderQuoteEligibility
{
    Task<ProviderQuoteIdentity> GetAsync(Guid userId,int categoryId,string bearer,CancellationToken ct);
    Task VerifyAsync(Guid providerId,int categoryId,string bearer,CancellationToken ct);
}
public interface IQuoteDisplayLookup
{
    Task<QuoteDisplay> GetAsync(int categoryId,int areaId,string bearer,CancellationToken ct);
}
public interface IQuoteRepository
{
    Task<QuoteContext?> ContextAsync(Guid requestId,CancellationToken ct);
    Task<QuoteDto> CreateAsync(Guid actor,Guid requestId,CreateQuoteRequest input,ProviderQuoteIdentity provider,QuoteDisplay display,int expectedRequestVersion,string hash,DateTime now,CancellationToken ct);
    Task<QuotePage> ListAsync(Guid actor,bool sent,string? status,Guid? propertyId,int page,int size,DateTime now,CancellationToken ct);
    Task<QuoteDto?> GetAsync(Guid actor,Guid quoteId,DateTime now,CancellationToken ct,bool? sent=null);
    Task<QuoteDto> DecideAsync(Guid actor,Guid quoteId,string decision,DateTime now,CancellationToken ct);
}
public sealed class QuoteForbiddenException() : Exception("You are not authorized to submit a quote for this request.");
public sealed class QuoteNotFoundException() : Exception("Quote or request not found.");
public sealed class QuoteConflictException(string message) : Exception(message);
public sealed class QuoteUnavailableException(string message) : Exception(message);
