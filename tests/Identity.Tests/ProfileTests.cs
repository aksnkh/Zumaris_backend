using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Features.Profile;
using ImageMagick;
using Xunit;
namespace Identity.Tests;

public class ProfileTests
{
    private static async Task<HttpClient> Login(AuthFactory factory)
    {
        var client = factory.NewClient();
        using var response = await client.PostAsJsonAsync("/api/app/login", new { email="alice@example.test",password="Password-for-tests-1" });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer",json.RootElement.GetProperty("payload").GetProperty("accessToken").GetString());
        return client;
    }
    private static async Task<JsonElement> Payload(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("payload").Clone();
    }
    [Fact]
    public async Task Profile_is_private_and_reads_current_account_email_without_creating_data()
    {
        using var factory = new AuthFactory(); using var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/profile/me")).StatusCode);
        using var client = await Login(factory);
        var body = await Payload(await client.GetAsync("/api/profile/me"));
        Assert.Equal(factory.Users.User.Id.ToString(),body.GetProperty("userId").GetString());
        Assert.Equal(factory.Users.User.Email,body.GetProperty("email").GetString());
        Assert.Equal("alice",body.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null,body.GetProperty("photoUrl").ValueKind);
        Assert.False(body.TryGetProperty("version",out _));
        Assert.Empty(factory.Profiles.Records);
    }
    [Fact]
    public async Task Updating_personal_information_normalizes_phone_and_last_write_wins_without_version()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        var body = await Payload(await client.PutAsJsonAsync("/api/profile/me",new {firstName=" James ",lastName=" Davidson ",phone="+44 7700 900123"}));
        Assert.Equal("James Davidson",body.GetProperty("displayName").GetString());
        Assert.Equal("+447700900123",body.GetProperty("phone").GetString());
        body = await Payload(await client.PutAsJsonAsync("/api/profile/me",new {firstName="Ali",lastName="Kamali",phone=(string?)null}));
        Assert.Equal("Ali Kamali",body.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null,body.GetProperty("phone").ValueKind);
        Assert.Equal(factory.Users.User.Email,body.GetProperty("email").GetString());
        Assert.Single(factory.Profiles.Records);
        // State is only for the token's subject, not a caller-selected user.
        Assert.True(factory.Profiles.Records.ContainsKey(factory.Users.User.Id));
    }
    [Theory]
    [InlineData("", "Davidson", "+447700900123")]
    [InlineData("James", "", "+447700900123")]
    [InlineData("James", "Davidson", "invalid")]
    [InlineData("James", "Davidson", "+01234567")]
    public async Task Invalid_fields_return_english_message_and_do_not_write(string firstName,string lastName,string phone)
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        using var response = await client.PutAsJsonAsync("/api/profile/me",new {firstName,lastName,phone});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
        Assert.Empty(factory.Profiles.Records);
    }
    [Fact]
    public async Task Email_and_user_identity_cannot_be_changed_through_profile_update()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        using var response = await client.PutAsJsonAsync("/api/profile/me",new {firstName="James",lastName="Davidson",email="other@example.test",userId=Guid.NewGuid()});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Empty(factory.Profiles.Records);
    }
    [Fact]
    public async Task Writes_require_csrf_and_revoked_sessions_cannot_read_profile()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        client.DefaultRequestHeaders.Remove("X-Zumaris-CSRF");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/profile/me",new {firstName="James",lastName="Davidson"})).StatusCode);
        await factory.Sessions.RevokeAllAsync(factory.Users.User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/profile/me")).StatusCode);
    }
    private static MultipartFormDataContent Photo(byte[] bytes,string name="photo.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType=new("image/png");
        form.Add(file,"Photo",name); return form;
    }
    [Fact]
    public async Task Photo_upload_read_replacement_and_delete_use_real_private_storage()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        using var image = new MagickImage(MagickColors.Red,800,600);
        using var upload = Photo(image.ToByteArray(MagickFormat.Png),"../../photo.png");
        var body = await Payload(await client.PostAsync("/api/profile/me/photo",upload));
        Assert.Equal("/identity/api/profile/me/photo",body.GetProperty("photoUrl").GetString());
        var files = Directory.GetFiles(factory.ProfilePhotoRoot,"*",SearchOption.AllDirectories);
        var first = Assert.Single(files); Assert.EndsWith(".png",first);
        using var download = await client.GetAsync("/api/profile/me/photo");
        Assert.Equal(HttpStatusCode.OK,download.StatusCode);
        Assert.Equal("image/png",download.Content.Headers.ContentType!.MediaType);
        Assert.True(download.Headers.CacheControl!.NoStore);
        using var decoded = new MagickImage(await download.Content.ReadAsByteArrayAsync());
        Assert.True(decoded.Width <= 512 && decoded.Height <= 512);
        using var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/profile/me/photo")).StatusCode);
        using var upload2 = Photo(image.ToByteArray(MagickFormat.Png));
        await Payload(await client.PostAsync("/api/profile/me/photo",upload2));
        Assert.False(File.Exists(first));
        Assert.Single(Directory.GetFiles(factory.ProfilePhotoRoot,"*",SearchOption.AllDirectories));
        body = await Payload(await client.DeleteAsync("/api/profile/me/photo"));
        Assert.Equal(JsonValueKind.Null,body.GetProperty("photoUrl").ValueKind);
        Assert.Empty(Directory.GetFiles(factory.ProfilePhotoRoot,"*",SearchOption.AllDirectories));
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/profile/me/photo")).StatusCode);
    }
    [Fact]
    public async Task Failed_photo_database_write_preserves_previous_photo_and_removes_new_file()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        using var image = new MagickImage(MagickColors.Blue,10,10);
        using var first = Photo(image.ToByteArray(MagickFormat.Png));
        await Payload(await client.PostAsync("/api/profile/me/photo",first));
        var path = Assert.Single(Directory.GetFiles(factory.ProfilePhotoRoot,"*",SearchOption.AllDirectories));
        factory.Profiles.FailPhotoWrite = true;
        using var replacement = Photo(image.ToByteArray(MagickFormat.Png));
        Assert.Equal(HttpStatusCode.InternalServerError,(await client.PostAsync("/api/profile/me/photo",replacement)).StatusCode);
        Assert.Equal(path,Assert.Single(Directory.GetFiles(factory.ProfilePhotoRoot,"*",SearchOption.AllDirectories)));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/profile/me/photo")).StatusCode);
    }
    [Fact]
    public async Task Another_account_cannot_read_the_first_accounts_profile_or_photo()
    {
        using var factory = new AuthFactory(); using var alice = await Login(factory);
        await Payload(await alice.PutAsJsonAsync("/api/profile/me",new {firstName="Alice",lastName="Test"}));
        var bobId = Guid.NewGuid();
        factory.Users.GoogleUsers.Add(new Zumaris.Identity.Domain.Entities.User {
            Id=bobId,UserName="bob",Email="bob@example.test",IsActive=true,
            PasswordHash=new Identity.Infrastructure.Security.PasswordHasher().Hash("Password-for-tests-1")
        });
        using var bob = factory.NewClient();
        var login = await Payload(await bob.PostAsJsonAsync("/api/app/login", new {email="bob@example.test",password="Password-for-tests-1"}));
        bob.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("accessToken").GetString());
        var profile = await Payload(await bob.GetAsync("/api/profile/me"));
        Assert.Equal(bobId.ToString(),profile.GetProperty("userId").GetString());
        Assert.Equal(JsonValueKind.Null,profile.GetProperty("firstName").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync("/api/profile/me/photo")).StatusCode);
        await Payload(await bob.PutAsJsonAsync("/api/profile/me",new {firstName="Bob",lastName="Test"}));
        Assert.Equal("Alice",factory.Profiles.Records[factory.Users.User.Id].FirstName);
        Assert.Equal("Bob",factory.Profiles.Records[bobId].FirstName);
    }
    [Fact]
    public async Task Fake_image_is_rejected_and_no_file_is_written()
    {
        using var factory = new AuthFactory(); using var client = await Login(factory);
        using var photo = Photo("not an image"u8.ToArray());
        using var response = await client.PostAsync("/api/profile/me/photo",photo);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Empty(factory.Profiles.Records);
        Assert.False(Directory.Exists(factory.ProfilePhotoRoot));
    }
    [Fact]
    public async Task Swagger_documents_profile_without_version()
    {
        using var factory = new AuthFactory(); using var client = factory.NewClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/profile/me").TryGetProperty("put",out _));
        Assert.True(paths.GetProperty("/api/profile/me/photo").TryGetProperty("post",out _));
        var properties = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UpdateProfileRequest").GetProperty("properties");
        Assert.False(properties.TryGetProperty("version",out _));
        Assert.False(properties.TryGetProperty("email",out _));
    }
}

public sealed class MemoryProfiles(MemoryUsers users) : IProfileRepository
{
    public Dictionary<Guid,ProfileRecord> Records { get; } = [];
    public bool FailPhotoWrite { get; set; }
    private readonly object gate = new();
    private ProfileRecord? Read(Guid user)
    {
        var account = users.User.Id==user ? users.User : users.GoogleUsers.SingleOrDefault(u=>u.Id==user);
        if (account is null || !account.IsActive) return null;
        return Records.TryGetValue(user,out var profile) ? profile with {Email=account.Email} : new(user,account.UserName,account.Email,null,null,null,null,null);
    }
    public Task<ProfileRecord?> GetAsync(Guid user,CancellationToken ct) { lock(gate) return Task.FromResult(Read(user)); }
    public Task<ProfileWrite> UpdateAsync(Guid user,UpdateProfileRequest input,DateTime now,CancellationToken ct)
    {
        lock(gate)
        {
            var current = Read(user) ?? throw new ProfileNotFoundException();
            var updated = current with {FirstName=input.FirstName,LastName=input.LastName,Phone=input.Phone,UpdatedAtUtc=now};
            Records[user]=updated; return Task.FromResult(new ProfileWrite(updated,null));
        }
    }
    public Task<ProfileWrite> SetPhotoAsync(Guid user,string? path,DateTime now,CancellationToken ct)
    {
        if (FailPhotoWrite) throw new IOException("Test database write failure.");
        lock(gate)
        {
            var current = Read(user) ?? throw new ProfileNotFoundException();
            Records[user]=current with {PhotoPath=path,UpdatedAtUtc=now};
            return Task.FromResult(new ProfileWrite(Records[user],current.PhotoPath));
        }
    }
}
