using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Zumaris.ServiceCatalog.Api.Controllers;

public static class CatalogIconFiles
{
    public static string? ContentType(string? name) => Path.GetExtension(name ?? "").ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp", ".svg" => "image/svg+xml", ".ico" => "image/x-icon", _ => null
    };
    public static bool IsValidName(string? name) => name is { Length: > 0 and <= 200 }
        && Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z") && ContentType(name) is not null;
}

// Public catalog assets, managed manually by the server administrator; no upload endpoint.
[ApiController]
[AllowAnonymous]
[Route("api/areas/icons")]
public sealed class AreaIconsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("{fileName}")]
    public IActionResult Get(string fileName)
    {
        if (!CatalogIconFiles.IsValidName(fileName)) return NotFound();
        var root = configuration["AreaIcons:Directory"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return NotFound();
        var path = Path.Combine(root, fileName);
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return PhysicalFile(path, CatalogIconFiles.ContentType(fileName)!, enableRangeProcessing: false);
    }
}
