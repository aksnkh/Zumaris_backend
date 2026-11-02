namespace Identity.Application.Features.Profile;

public sealed record ProfileRecord(Guid UserId, string UserName, string Email, string? FirstName,
    string? LastName, string? Phone, string? PhotoPath, DateTime? UpdatedAtUtc);
public sealed record ProfileResponse(Guid UserId, string UserName, string? FirstName, string? LastName,
    string DisplayName, string Email, string? Phone, string? PhotoUrl, DateTime? UpdatedAtUtc);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfileRequest(string FirstName, string LastName, string? Phone);
public sealed record ProfilePhotoUpload(Stream Content, long Length);
public sealed record StoredProfilePhoto(string FilePath);
public sealed record ProfilePhotoDownload(Stream Content, string ContentType, string FileName);
public sealed record ProfileWrite(ProfileRecord Profile, string? PreviousPhotoPath);

public interface IProfileRepository
{
    Task<ProfileRecord?> GetAsync(Guid userId, CancellationToken ct);
    Task<ProfileWrite> UpdateAsync(Guid userId, UpdateProfileRequest request, DateTime now, CancellationToken ct);
    Task<ProfileWrite> SetPhotoAsync(Guid userId, string? path, DateTime now, CancellationToken ct);
}
public interface IProfilePhotoStorage
{
    Task<StoredProfilePhoto> SaveAsync(Guid userId, ProfilePhotoUpload photo, CancellationToken ct);
    Task DeleteAsync(string path);
    Task<ProfilePhotoDownload> OpenAsync(string path, CancellationToken ct);
}
public sealed class ProfileValidationException(Dictionary<string,string[]> errors) : Exception("Profile validation failed.")
{
    public Dictionary<string,string[]> Errors { get; } = errors;
}
public sealed class ProfileNotFoundException() : Exception("Profile or photo not found.");
