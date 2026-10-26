using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Zumaris.ServiceCatalog.Api.Controllers;

// Public catalog assets, managed manually by the server administrator; no upload endpoint.
[ApiController]
[AllowAnonymous]
[Route("api/categories/icons")]
public sealed class CategoryIconsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("{fileName}")]
    public IActionResult Get(string fileName)
    {
        if (!CatalogIconFiles.IsValidName(fileName)) return NotFound();
        var root = configuration["CategoryIcons:Directory"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return NotFound();
        var path = Path.Combine(root, fileName);
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return PhysicalFile(path, CatalogIconFiles.ContentType(fileName)!, enableRangeProcessing: false);
    }
}
