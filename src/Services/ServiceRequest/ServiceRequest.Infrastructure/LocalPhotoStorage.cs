using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ImageMagick;
using Zumaris.ServiceRequests.Application;

namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class LocalPhotoStorage(IConfiguration configuration, ILogger<LocalPhotoStorage> logger) : IPhotoStorage
{
    static LocalPhotoStorage()
    {
        ResourceLimits.Memory = 256 * 1024 * 1024;
        ResourceLimits.Width = 10000;
        ResourceLimits.Height = 10000;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 2;
    }
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private readonly string root = ValidateRoot(configuration["Storage:RootPath"]);
    private static string ValidateRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Storage:RootPath must be an absolute, private writable directory.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }
    private static RequestValidationException Invalid(string message) => new(new() { ["Photos"] = [message] });

    public async Task<StoredPhoto> SaveAsync(Guid requestId, PhotoUpload photo, CancellationToken ct)
    {
        if (photo.Length is < 1 or > MaxFileBytes) throw Invalid("Each JPEG/PNG photo must be 1 byte to 5 MiB.");
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await photo.Content.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > MaxFileBytes) throw Invalid("Photo exceeds 5 MiB.");
            await bytes.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (bytes.Length == 0 || bytes.Length != photo.Length) throw Invalid("Photo length is invalid.");
        string? temporary = null;
        try
        {
            var data = bytes.ToArray();
            var png = data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var jpeg = data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255;
            if (!png && !jpeg) throw Invalid("Only decoded JPEG and PNG images are accepted.");
            var format = png ? MagickFormat.Png : MagickFormat.Jpeg;
            var settings = new MagickReadSettings { Format = format, FrameCount = 1 };
            using (var info = new MagickImage())
            {
                info.Ping(data, settings);
                if (info.Width > 10000 || info.Height > 10000 || (long)info.Width * info.Height > 12_000_000)
                    throw Invalid("Photo dimensions exceed 12 megapixels or 10000 pixels per side.");
            }
            ct.ThrowIfCancellationRequested();
            using var image = new MagickImage(data, settings);
            image.AutoOrient(); image.Strip(); image.Format = format; image.Quality = 85;
            var id = Guid.NewGuid();
            var relative = $"service-requests/{requestId:D}/{id:N}.{(format == MagickFormat.Png ? "png" : "jpg")}";
            var full = Resolve(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            temporary = full + ".upload-" + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                image.Write(output);
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, full); temporary = null;
            return new(id, relative);
        }
        catch (Exception ex) when (ex is MagickException or NotSupportedException)
        { throw Invalid("The image could not be decoded as a valid JPEG/PNG."); }
        finally
        {
            if (temporary is not null) await DeletePhysical(temporary);
        }
    }

    private string Resolve(string relative)
    {
        if (!Regex.IsMatch(relative, "^service-requests/[0-9a-f-]{36}/[0-9a-f]{32}\\.(jpg|png)$", RegexOptions.CultureInvariant))
            throw new RequestNotFoundException();
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new RequestNotFoundException();
        return full;
    }
    private Task DeletePhysical(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { logger.LogError(ex, "Photo cleanup failed; reconcile the private upload directory."); }
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string relativePath) => DeletePhysical(Resolve(relativePath));
    public Task<PhotoDownload> OpenAsync(string relativePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var full = Resolve(relativePath);
        try
        {
            Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return Task.FromResult(new PhotoDownload(stream, relativePath.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg", Path.GetFileName(full)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new RequestNotFoundException(); }
    }
}
