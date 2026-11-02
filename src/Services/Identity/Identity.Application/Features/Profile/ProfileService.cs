using System.Text;
using System.Text.RegularExpressions;
namespace Identity.Application.Features.Profile;

public sealed class ProfileService(IProfileRepository repository, IProfilePhotoStorage storage, TimeProvider clock)
{
    private static ProfileResponse Response(ProfileRecord record)
    {
        var displayName = string.Join(" ", new[] {record.FirstName, record.LastName}.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new(record.UserId, record.UserName, record.FirstName, record.LastName,
            displayName.Length == 0 ? record.UserName : displayName, record.Email, record.Phone,
            record.PhotoPath is null ? null : "/identity/api/profile/me/photo", record.UpdatedAtUtc);
    }
    public async Task<ProfileResponse> GetAsync(Guid userId, CancellationToken ct) =>
        Response(await repository.GetAsync(userId, ct) ?? throw new ProfileNotFoundException());

    public async Task<ProfileResponse> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string,string[]>();
        string Name(string? value, string field)
        {
            var name = value?.Trim() ?? "";
            if (name.EnumerateRunes().Count() is < 1 or > 100 || name.Any(char.IsControl))
                errors[field] = [$"{field} must contain 1–100 characters without control characters."];
            return name;
        }
        var first = Name(request.FirstName,"FirstName");
        var last = Name(request.LastName,"LastName");
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        // International number required when supplied; spaces, parentheses and hyphens are presentation only.
        if (phone is not null)
        {
            if (phone.Length > 40 || !Regex.IsMatch(phone,@"\A\+[0-9 ()-]+\z"))
                errors["Phone"] = ["Enter an international phone number starting with +, or leave it empty."];
            else
            {
                phone = "+" + new string(phone.Where(char.IsAsciiDigit).ToArray());
                if (!Regex.IsMatch(phone,@"\A\+[1-9][0-9]{6,14}\z"))
                    errors["Phone"] = ["The phone number must contain 7–15 digits and a valid country code."];
            }
        }
        if (errors.Count > 0) throw new ProfileValidationException(errors);
        var write = await repository.UpdateAsync(userId,new(first,last,phone),clock.GetUtcNow().UtcDateTime,ct);
        return Response(write.Profile);
    }
    public async Task<ProfileResponse> UploadPhotoAsync(Guid userId, ProfilePhotoUpload photo, CancellationToken ct)
    {
        _ = await repository.GetAsync(userId,ct) ?? throw new ProfileNotFoundException();
        var saved = await storage.SaveAsync(userId,photo,ct);
        ProfileWrite write;
        try { write = await repository.SetPhotoAsync(userId,saved.FilePath,clock.GetUtcNow().UtcDateTime,ct); }
        catch { await storage.DeleteAsync(saved.FilePath); throw; }
        if (write.PreviousPhotoPath is not null) await storage.DeleteAsync(write.PreviousPhotoPath);
        return Response(write.Profile);
    }
    public async Task<ProfileResponse> DeletePhotoAsync(Guid userId, CancellationToken ct)
    {
        var write = await repository.SetPhotoAsync(userId,null,clock.GetUtcNow().UtcDateTime,ct);
        if (write.PreviousPhotoPath is not null) await storage.DeleteAsync(write.PreviousPhotoPath);
        return Response(write.Profile);
    }
    public async Task<ProfilePhotoDownload> DownloadPhotoAsync(Guid userId, CancellationToken ct)
    {
        var current = await repository.GetAsync(userId,ct) ?? throw new ProfileNotFoundException();
        if (current.PhotoPath is null) throw new ProfileNotFoundException();
        return await storage.OpenAsync(current.PhotoPath,ct);
    }
}
