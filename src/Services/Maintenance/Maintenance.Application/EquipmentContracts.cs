using System.ComponentModel.DataAnnotations;
namespace Zumaris.Maintenance.Application;
public sealed record EquipmentInput([Required, StringLength(150, MinimumLength = 1)] string Name, [Required] string Category, [Required] string Status,
 [StringLength(100)] string? Brand = null, [StringLength(150)] string? Model = null, [StringLength(150)] string? SerialNumber = null,
 DateOnly? InstalledOn = null, DateOnly? WarrantyStartsOn = null, DateOnly? WarrantyExpiresOn = null, [StringLength(200)] string? Location = null,
 DateOnly? LastServicedOn = null, DateOnly? NextServiceOn = null, IReadOnlyList<Guid>? ServiceRequestIds = null);
public sealed record Equipment(Guid EquipmentId, Guid PropertyId, EquipmentInput Details, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool Archived)
{
    public string RelatedDocumentsUrl => $"/document/api/properties/{PropertyId}/documents?equipmentId={EquipmentId}";
    public IReadOnlyList<string> ServiceRequestUrls => (Details.ServiceRequestIds ?? []).Select(id => $"/service-request/api/service-requests/{id}").ToArray();
}
public sealed record EquipmentEvent(Guid EventId, Guid PropertyId, Guid EquipmentId, string Type, string Title, DateTime OccurredAtUtc);
public interface IEquipmentRepository
{
    Task<(IReadOnlyList<Equipment> Items, long Total)> List(Guid property, int page, int size, string? category, string? status, bool archived, CancellationToken ct);
    Task<Equipment?> Get(Guid property, Guid id, CancellationToken ct);
    Task<Equipment> Save(Guid property, Guid? id, EquipmentInput input, CancellationToken ct);
    Task Archive(Guid property, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<EquipmentEvent> Items, long Total)> Events(Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct);
}
