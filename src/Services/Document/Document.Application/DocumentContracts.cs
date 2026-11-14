using System.ComponentModel.DataAnnotations;
namespace Zumaris.Document.Application;
public sealed record DocumentInput([Required, StringLength(200, MinimumLength = 1)] string Title, [Required] string Type, DateOnly? ExpiresOn = null, Guid? EquipmentId = null, Guid? RequestId = null);
public sealed record PropertyDocument(Guid DocumentId, Guid PropertyId, DocumentInput Details, string FileName, string ContentType, long SizeBytes, string Sha256, DateTime UploadedAtUtc, DateTime? UpdatedAtUtc, bool Archived)
{ public string DownloadUrl => $"/document/api/properties/{PropertyId}/documents/{DocumentId}/file"; }
public sealed record DocumentEvent(Guid EventId, Guid PropertyId, Guid DocumentId, string Type, string Title, DateTime OccurredAtUtc);
public interface IDocumentRepository
{
    Task<(IReadOnlyList<PropertyDocument> Items, long Total)> List(Guid property, int page, int size, string? type, Guid? equipment, Guid? request, bool archived, CancellationToken ct);
    Task<PropertyDocument?> Get(Guid property, Guid id, CancellationToken ct);
    Task<PropertyDocument> Create(Guid property, Guid id, DocumentInput input, string fileName, string contentType, long size, string sha256, CancellationToken ct);
    Task<PropertyDocument> Update(Guid property, Guid id, DocumentInput input, CancellationToken ct);
    Task Archive(Guid property, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<DocumentEvent> Items, long Total)> Events(Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct);
}
