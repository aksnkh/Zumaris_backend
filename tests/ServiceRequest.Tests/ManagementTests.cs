using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;
using Xunit;

namespace ServiceRequest.Tests;

public sealed class ManagementTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static async Task<Guid> Create(RequestFactory factory, HttpClient client)
    {
        using var r = await client.PostAsJsonAsync("/api/service-requests", RequestFactory.Valid(), Json);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return factory.Repository.Records.Last().Request.RequestId;
    }
    private static UpdateServiceRequest Update(CreateServiceRequest r, int version) => new(r.PropertyId, r.CategoryId, r.AreaTypeId,
        "The kitchen repair details have now been updated.", r.Urgency, r.PreferredDate, r.PreferredStartTime, r.PreferredEndTime, version);
    private static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.Teal, 3, 3);
        image.Format = MagickFormat.Png; return image.ToByteArray();
    }
    private static MultipartFormDataContent Form(int version, params byte[][] files)
    {
        var form = new MultipartFormDataContent(); form.Add(new StringContent(version.ToString()), "Version");
        foreach (var bytes in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("image/png");
            form.Add(content, "Photos", "../../untrusted-file.png");
        }
        return form;
    }

    [Fact]
    public async Task List_details_and_timeline_are_owner_scoped()
    {
        using var f = new RequestFactory(); using var owner = f.Client(); var id = await Create(f, owner);
        using var other = f.Client(userId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/service-requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/service-requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/service-requests/{id}/timeline")).StatusCode);
        var list = await other.GetFromJsonAsync<RequestPage>("/api/service-requests", Json);
        Assert.Empty(list!.Items); Assert.Equal(0, list.TotalCount);
        var events = await owner.GetFromJsonAsync<List<RequestStatusHistoryDto>>($"/api/service-requests/{id}/timeline", Json);
        Assert.Equal(RequestStatus.Submitted, Assert.Single(events!).ToStatus);
    }

    [Fact]
    public async Task Update_uses_version_then_cancel_adds_history_and_blocks_further_edits()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        var body = Update(RequestFactory.Valid(), 1);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/service-requests/{id}", body, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/service-requests/{id}", body, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/service-requests/{id}/cancel", new { reason = "No longer needed", version = 2 })).StatusCode);
        var record = Assert.Single(f.Repository.Records).Request;
        Assert.Equal(RequestStatus.Cancelled, record.Status); Assert.Equal(3, record.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/service-requests/{id}", body with { Version = 3 }, Json)).StatusCode);
        Assert.Equal(2, f.Repository.History.Count);
        Assert.Equal("No longer needed", f.Repository.History.Last().Event.Reason);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(1, 0)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        using var f = new RequestFactory(); using var c = f.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/service-requests?page={page}&pageSize={size}")).StatusCode);
    }

    [Fact]
    public async Task Filtering_pagination_and_total_count_are_correct()
    {
        using var f = new RequestFactory(); using var c = f.Client();
        var first = await Create(f, c); await Create(f, c); await Create(f, c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/service-requests/{first}/cancel", new { reason = "Changed plans", version = 1 })).StatusCode);
        var page = await c.GetFromJsonAsync<RequestPage>("/api/service-requests?status=Submitted&page=2&pageSize=1", Json);
        Assert.Equal(2, page!.TotalCount); Assert.Single(page.Items); Assert.Equal(2, page.Page);
    }

    [Fact]
    public async Task Photo_upload_writes_valid_image_and_only_relative_path_and_requires_owner_to_download()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var form = Form(1, Png());
        using var response = await c.PostAsync($"/api/service-requests/{id}/photos", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PhotoUploadResult>(Json);
        Assert.Equal(2, result!.Version);
        var photo = Assert.Single(f.Repository.Photos);
        Assert.StartsWith($"service-requests/{id}/", photo.FilePath);
        Assert.DoesNotContain("untrusted", photo.FilePath); Assert.DoesNotContain("..", photo.FilePath);
        Assert.True(File.Exists(Path.Combine(f.UploadRoot, photo.FilePath)));
        var url = "/" + result.Photos.Single().DownloadPath;
        using var download = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", download.Headers.CacheControl!.ToString());
        using var decoded = new MagickImage(await download.Content.ReadAsByteArrayAsync()); Assert.Equal(3u, decoded.Width);
        using var other = f.Client(userId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Eight_photo_limit_and_stale_versions_are_enforced()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var first = Form(1, Enumerable.Range(0, 8).Select(_ => Png()).ToArray());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/service-requests/{id}/photos", first)).StatusCode);
        using var extra = Form(2, Png());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/service-requests/{id}/photos", extra)).StatusCode);
        using var stale = Form(1, Png());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/service-requests/{id}/photos", stale)).StatusCode);
        Assert.Equal(8, f.Repository.Photos.Count); Assert.Equal(8, Directory.GetFiles(f.UploadRoot, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Invalid_image_after_valid_image_cleans_entire_batch_before_database()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var form = Form(1, Png(), "not-an-image"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/service-requests/{id}/photos", form)).StatusCode);
        Assert.Empty(f.Repository.Photos); Assert.Empty(Directory.GetFiles(f.UploadRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(1, f.Repository.Records.Single().Request.Version);
    }

    [Fact]
    public async Task Unauthorized_and_cancelled_requests_cannot_write_files()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var other = f.Client(userId: Guid.NewGuid()); using var form = Form(1, Png());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/service-requests/{id}/photos", form)).StatusCode);
        await c.PostAsJsonAsync($"/api/service-requests/{id}/cancel", new { reason = "Cancelled by customer", version = 1 });
        using var cancelled = Form(2, Png());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/service-requests/{id}/photos", cancelled)).StatusCode);
        Assert.False(Directory.Exists(f.UploadRoot));
    }

    [Fact]
    public async Task Known_database_conflict_compensates_files_but_unknown_commit_failure_retains_them()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        f.Repository.ConflictOnAttach = true;
        using var first = Form(1, Png());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/service-requests/{id}/photos", first)).StatusCode);
        Assert.Empty(Directory.GetFiles(f.UploadRoot, "*", SearchOption.AllDirectories));
        f.Repository.ConflictOnAttach = false; f.Repository.FailOnAttach = true;
        using var second = Form(1, Png());
        Assert.Equal(HttpStatusCode.InternalServerError, (await c.PostAsync($"/api/service-requests/{id}/photos", second)).StatusCode);
        Assert.Single(Directory.GetFiles(f.UploadRoot, "*", SearchOption.AllDirectories));
        Assert.Empty(f.Repository.Photos);
    }

    [Fact]
    public async Task Swagger_describes_all_routes_and_multipart_photos()
    {
        using var f = new RequestFactory(); using var c = f.Client(false);
        using var json = JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/service-requests", "/api/service-requests/{id}", "/api/service-requests/{id}/cancel", "/api/service-requests/{id}/timeline", "/api/service-requests/{id}/photos", "/api/service-requests/{id}/photos/{photoId}" })
            Assert.True(paths.TryGetProperty(path, out _));
        Assert.True(paths.GetProperty("/api/service-requests/{id}/photos").GetProperty("post").GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
    }

    [Fact]
    public async Task Concurrent_batches_cannot_exceed_eight_and_rejected_files_are_removed()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var first = Form(1, Enumerable.Range(0, 5).Select(_ => Png()).ToArray());
        using var second = Form(1, Enumerable.Range(0, 5).Select(_ => Png()).ToArray());
        var responses = await Task.WhenAll(c.PostAsync($"/api/service-requests/{id}/photos", first), c.PostAsync($"/api/service-requests/{id}/photos", second));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(5, f.Repository.Photos.Count);
        Assert.Equal(5, Directory.GetFiles(f.UploadRoot, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Rejects_oversized_file_and_valid_png_with_oversized_dimensions_before_writing()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var huge = Form(1, new byte[5 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/service-requests/{id}/photos", huge)).StatusCode);
        // A real 10001 x 1 RGB PNG, generated with zlib and correct PNG chunk CRCs.
        var wide = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAJxEAAAABCAIAAAAYfkhiAAAANElEQVR4nO3BMQEAAADCoPVPbQlPoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAfgZ1NAABOkZuYgAAAABJRU5ErkJggg==");
        using var dimensions = Form(1, wide);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/service-requests/{id}/photos", dimensions)).StatusCode);
        Assert.Empty(f.Repository.Photos); Assert.False(Directory.Exists(f.UploadRoot));
    }

    [Fact]
    public async Task Jpeg_is_recognized_by_content_and_downloaded_with_correct_type()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var jpeg = new MagickImage(MagickColors.Red, 2, 2); jpeg.Format = MagickFormat.Jpeg;
        using var form = Form(1, jpeg.ToByteArray()); // Form deliberately calls the file .png.
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/service-requests/{id}/photos", form)).StatusCode);
        var photo = Assert.Single(f.Repository.Photos); Assert.EndsWith(".jpg", photo.FilePath);
        using var response = await c.GetAsync($"/api/service-requests/{id}/photos/{photo.Id}");
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Traversal_path_from_bad_metadata_cannot_read_files_outside_upload_root()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        var photoId = Guid.NewGuid();
        f.Repository.Photos.Add(new(photoId, id, "../../appsettings.json", 0, DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/service-requests/{id}/photos/{photoId}")).StatusCode);
    }

    [Theory]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.Completed)]
    public async Task Cannot_cancel_or_attach_to_started_or_completed_request(RequestStatus status)
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        f.Repository.Records[0] = f.Repository.Records[0] with { Request = f.Repository.Records[0].Request with { Status = status } };
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/service-requests/{id}/cancel", new { reason = "Changed plans", version = 1 })).StatusCode);
        using var form = Form(1, Png());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/service-requests/{id}/photos", form)).StatusCode);
        Assert.Single(f.Repository.History); Assert.False(Directory.Exists(f.UploadRoot));
    }

    [Fact]
    public async Task Other_users_cannot_update_or_cancel_and_invalid_reason_is_rejected()
    {
        using var f = new RequestFactory(); using var c = f.Client(); var id = await Create(f, c);
        using var other = f.Client(userId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/service-requests/{id}", Update(RequestFactory.Valid(), 1), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/service-requests/{id}/cancel", new { reason = "Changed plans", version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/service-requests/{id}/cancel", new { reason = " ", version = 1 })).StatusCode);
        Assert.Equal(1, f.Repository.Records.Single().Request.Version);
    }
}
