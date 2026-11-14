using System.ComponentModel.DataAnnotations;
namespace Zumaris.Property.Application.Features;
public sealed record RoomInput([Required, StringLength(100, MinimumLength = 1)] string Name, [Required] string Type, [StringLength(50)] string? Floor = null, decimal? FloorAreaSquareMetres = null);
public sealed record PropertyRoom(Guid RoomId, Guid PropertyId, RoomInput Details, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool Archived);
public sealed record PassportProfile(decimal? FloorAreaSquareMetres);
public sealed record ManualEventInput(DateTimeOffset EventAt, [Required, StringLength(100, MinimumLength = 1)] string Title, [StringLength(4000)] string? Description = null, decimal? Cost = null, string? CurrencyCode = null);
public sealed record ManualPropertyEvent(Guid EventId, Guid PropertyId, ManualEventInput Details, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool Archived);
public interface IPassportRepository
{
    Task<PassportProfile> Profile(Guid property, CancellationToken ct);
    Task SetProfile(Guid property, PassportProfile input, CancellationToken ct);
    Task<(IReadOnlyList<PropertyRoom> Items, long Total)> Rooms(Guid property, int page, int size, bool archived, CancellationToken ct);
    Task<PropertyRoom?> Room(Guid property, Guid id, CancellationToken ct);
    Task<PropertyRoom> SaveRoom(Guid property, Guid? id, RoomInput input, CancellationToken ct);
    Task ArchiveRoom(Guid property, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<ManualPropertyEvent> Items, long Total)> Events(Guid property, int page, int size, DateTime? from, DateTime? to, CancellationToken ct);
    Task<ManualPropertyEvent?> Event(Guid property, Guid id, CancellationToken ct);
    Task<ManualPropertyEvent> SaveEvent(Guid property, Guid? id, ManualEventInput input, CancellationToken ct);
    Task ArchiveEvent(Guid property, Guid id, CancellationToken ct);
}
