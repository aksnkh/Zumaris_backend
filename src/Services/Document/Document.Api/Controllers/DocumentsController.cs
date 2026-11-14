using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Document.Application;
namespace Zumaris.Document.Api.Controllers;
public sealed class DocumentUpload
{
    [Required, StringLength(200, MinimumLength = 1)] public string Title { get; set; } = "";
    [Required] public string Type { get; set; } = "";
    public DateOnly? ExpiresOn { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid? RequestId { get; set; }
    [Required] public IFormFile File { get; set; } = null!;
}
[ApiController, Authorize, ServiceFilter(typeof(PassportExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/properties/{propertyId:guid}/documents")]
public sealed class DocumentsController(IDocumentRepository repo, PassportHttpClient access, IConfiguration config) : ControllerBase
{
    public static readonly string[] Types = ["Certificate", "Invoice", "Report", "Warranty", "Manual", "Insurance", "FloorPlan", "Other"];
    [HttpGet("options")] public IActionResult Options() => Ok(PassportResponse.Of(new { types = Types, contentTypes = new[] { "application/pdf", "image/jpeg", "image/png" }, maxFileBytes = 20 * 1024 * 1024, archiveRetainsFile = true }));
    [HttpGet] public async Task<IActionResult> List(Guid propertyId, int pageNumber = 1, int pageSize = 20, string? type = null, Guid? equipmentId = null, Guid? requestId = null, bool archived = false, CancellationToken ct = default) { new EventQuery(pageNumber, pageSize).Validate(); if (type is not null && !Types.Contains(type)) throw new PassportException(400, "Use a supported document type."); await access.RequireProperty(propertyId, false, ct); var r = await repo.List(propertyId, pageNumber, pageSize, type, equipmentId, requestId, archived, ct); return Ok(PassportResponse.Of(new PassportPage<PropertyDocument>(r.Items, r.Total, pageNumber, pageSize))); }
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid propertyId, Guid id, CancellationToken ct) { await access.RequireProperty(propertyId, false, ct); return Ok(PassportResponse.Of(await Find(propertyId, id, ct))); }
    [HttpPost, RequestSizeLimit(22 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid propertyId, [FromForm] DocumentUpload form, CancellationToken ct)
    {
        await access.RequireProperty(propertyId, true, ct); var input = new DocumentInput(form.Title, form.Type, form.ExpiresOn, form.EquipmentId, form.RequestId); await Validate(propertyId, input, ct);
        if (form.File.Length is < 1 or > 20 * 1024 * 1024) throw new PassportException(400, "Provide a PDF, JPEG or PNG file up to 20 MiB.");
        var name = Path.GetFileName(form.File.FileName.Replace('\\', '/')); if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new PassportException(400, "Use a valid file name up to 200 characters.");
        using var memory = new MemoryStream(); await form.File.CopyToAsync(memory, ct); var bytes = memory.ToArray(); if (bytes.Length != form.File.Length) throw new PassportException(400, "The uploaded file is incomplete.");
        string? mime = bytes.AsSpan().StartsWith("%PDF-"u8) ? "application/pdf" : bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png" : bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : null;
        if (mime is null) throw new PassportException(400, "Only PDF, JPEG and PNG files are supported.");
        var id = Guid.NewGuid(); var path = FilePath(propertyId, id, true); bool metadataAttempted = false;
        try { await System.IO.File.WriteAllBytesAsync(path, bytes, ct); metadataAttempted = true;
            var r = await repo.Create(propertyId, id, input, name, mime, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), ct);  return StatusCode(201, PassportResponse.Of(r, "Document uploaded.", 201)); }
        finally { // A database commit may have succeeded even if its acknowledgement was lost.
            // Retain the file once metadata was attempted; reconcile orphan files after an outage.
            if (!metadataAttempted && System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid propertyId, Guid id, DocumentInput input, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); var old = await Find(propertyId, id, ct); if (old.Archived) throw new PassportException(409, "Archived documents cannot be edited."); await Validate(propertyId, input, ct); try { return Ok(PassportResponse.Of(await repo.Update(propertyId, id, input, ct), "Document updated.")); } catch (KeyNotFoundException) { throw new PassportException(409, "Document changed or was archived. Reload before retrying."); } }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Archive(Guid propertyId, Guid id, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); await Find(propertyId, id, ct); await repo.Archive(propertyId, id, ct); return NoContent(); }
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> FileBytes(Guid propertyId, Guid id, bool preview = false, CancellationToken ct = default)
    {
        await access.RequireProperty(propertyId, false, ct); var d = await Find(propertyId, id, ct); var path = FilePath(propertyId, id, false); if (!System.IO.File.Exists(path)) throw new PassportException(503, "The document file is temporarily unavailable.");
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        if (preview) { Response.Headers.ContentDisposition = "inline"; return PhysicalFile(path, d.ContentType, enableRangeProcessing: true); }
        return PhysicalFile(path, d.ContentType, d.FileName, enableRangeProcessing: true);
    }
    [HttpGet("events")] public async Task<IActionResult> Events(Guid propertyId, [FromQuery] EventQuery query, CancellationToken ct) { query.Validate(); await access.RequireProperty(propertyId, false, ct); var r = await repo.Events(propertyId, query.PageNumber, query.PageSize, query.From?.UtcDateTime, query.To?.UtcDateTime, query.Type, ct); return Ok(PassportResponse.Of(new PassportPage<PassportEvent>(r.Items.Select(e => new PassportEvent("document:" + e.EventId, propertyId, e.OccurredAtUtc, "Document", e.Type, e.Title, null, e.DocumentId)).ToList(), r.Total, query.PageNumber, query.PageSize))); }
    private async Task<PropertyDocument> Find(Guid p, Guid id, CancellationToken ct) => await repo.Get(p, id, ct) ?? throw new PassportException(404, "Document not found.");
    private async Task Validate(Guid property, DocumentInput input, CancellationToken ct)
    {
        PassportResponse.Validate(input); if (!Types.Contains(input.Type)) throw new PassportException(400, "Use a supported document type.");
        if (input.EquipmentId.HasValue) { var e = await access.GetAsync<Response<JsonElement>>("Maintenance", $"api/properties/{property}/equipment/{input.EquipmentId}", ct); if (e.Payload.GetProperty("archived").GetBoolean()) throw new PassportException(409, "Cannot link archived equipment."); }
        if (input.RequestId.HasValue) { var r = await access.GetAsync<JsonElement>("ServiceRequest", $"api/service-requests/{input.RequestId}", ct); if (r.GetProperty("request").GetProperty("propertyId").GetGuid() != property) throw new PassportException(400, "The related request must belong to this property."); }
    }
    private string FilePath(Guid p, Guid id, bool create)
    {
        string? root = config["Documents:RootPath"]; if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new PassportException(503, "Document storage is not configured.");
        var dir = Path.Combine(root, p.ToString("N")); if (create) Directory.CreateDirectory(dir);
        foreach (var d in new[] { root, dir }) if (Directory.Exists(d) && (System.IO.File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) throw new PassportException(503, "Document storage is unavailable.");
        var path = Path.Combine(dir, id.ToString("N")); if (System.IO.File.Exists(path) && (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new PassportException(503, "Document storage is unavailable."); return path;
    }
}
