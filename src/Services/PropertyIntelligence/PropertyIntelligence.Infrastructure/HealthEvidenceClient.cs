using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.PropertyIntelligence.Application;
using Zumaris.PropertyIntelligence.Domain;
namespace Zumaris.PropertyIntelligence.Infrastructure;
public sealed class HealthEvidenceClient(PassportHttpClient http) : IHealthEvidenceClient
{
    public sealed record EquipmentDetails(string Name, string Category, string Status, DateOnly? LastServicedOn, DateOnly? NextServiceOn);
    public sealed record EquipmentWire(Guid EquipmentId, Guid PropertyId, EquipmentDetails Details, bool Archived);
    public sealed record DocumentDetails(string Type, DateOnly? ExpiresOn);
    public sealed record DocumentWire(Guid DocumentId, Guid PropertyId, DocumentDetails Details, DateTime UploadedAtUtc, DateTime? UpdatedAtUtc, bool Archived);
    private async Task<IReadOnlyList<T>> Pages<T>(string service, string path, CancellationToken ct)
    {
        var result = new List<T>();
        for (int page = 1; page <= 100; page++)
        {
            var response = await http.GetAsync<Response<PassportPage<T>>>(service, path + $"?archived=false&pageNumber={page}&pageSize=100", ct);
            var data = response.Payload;
            if (data is null || data.Items is null || data.TotalCount < 0 || data.TotalCount > 10000 || data.Items.Any(x => x is null)) throw new PassportException(503, "Health evidence could not be loaded completely.");
            result.AddRange(data.Items);
            if (result.Count >= data.TotalCount) return result;
            if (data.Items.Count == 0) throw new PassportException(503, "Health evidence changed while loading. Please retry.");
        }
        throw new PassportException(503, "Health evidence exceeds the supported evaluation size.");
    }
    public async Task<HealthEvidence> Load(Guid property, CancellationToken ct)
    {
        var e = Pages<EquipmentWire>("Maintenance", $"api/properties/{property}/equipment", ct);
        var d = Pages<DocumentWire>("Document", $"api/properties/{property}/documents", ct);
        await Task.WhenAll(e, d);
        if (e.Result.Any(x => x.PropertyId != property || x.EquipmentId == Guid.Empty || x.Details is null || x.Archived) || d.Result.Any(x => x.PropertyId != property || x.DocumentId == Guid.Empty || x.Details is null || x.Archived) || e.Result.Select(x => x.EquipmentId).Distinct().Count() != e.Result.Count || d.Result.Select(x => x.DocumentId).Distinct().Count() != d.Result.Count)
            throw new PassportException(503, "Health evidence returned invalid property records.");
        return new(e.Result.Select(x => new EquipmentEvidence(x.EquipmentId, x.Details.Name, x.Details.Category, x.Details.Status, x.Details.LastServicedOn, x.Details.NextServiceOn)).ToArray(), d.Result.Select(x => new DocumentEvidence(x.DocumentId, x.Details.Type, x.Details.ExpiresOn, x.UploadedAtUtc, x.UpdatedAtUtc)).ToArray());
    }
}
