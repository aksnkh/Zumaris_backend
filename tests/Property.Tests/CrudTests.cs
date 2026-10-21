using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Zumaris.Property.Application.Features;
using Zumaris.Property.Domain.Entities;
using Xunit;

namespace Property.Tests;

public sealed class CrudTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web)
    { Converters={new JsonStringEnumConverter()} };
    private static async Task<PropertyDto> Create(HttpClient c,CreateProperty? input=null)
    {
        using var response=await c.PostAsJsonAsync("/api/properties",input??PropertyFactory.Valid(),Json);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        Assert.Equal("no-store, no-cache",response.Headers.CacheControl!.ToString());
        var x=(await response.Content.ReadFromJsonAsync<PropertyDto>(Json))!;
        Assert.Equal("properties/"+x.PropertyId,response.Headers.Location!.ToString());return x;
    }
    private static UpdateProperty Update(PropertyDto x,OwnershipRole role=OwnershipRole.Tenant)=>new(x.AddressLine,x.Postcode,x.City,x.CountryCode,
        x.Latitude,x.Longitude,x.PropertyType,4,x.Bathrooms,x.YearBuilt,role,x.Version);
    [Fact]
    public async Task Create_uses_jwt_identity_normalizes_address_and_returns_unknown_summary_as_null()
    {
        using var f=new PropertyFactory();using var c=f.Client();
        var p=await Create(c,PropertyFactory.Valid() with { AddressLine="  12 Example Road  ",CountryCode="gb",Postcode="nw11 8hb" });
        Assert.Equal("12 Example Road",p.AddressLine);Assert.Equal("GB",p.CountryCode);Assert.Equal("NW11 8HB",p.Postcode);
        Assert.True(p.IsCurrent);Assert.Equal(1,p.Version);Assert.Equal(f.UserId,f.Repository.Records[p.PropertyId].CreatedByUserId);
        Assert.Null(p.HealthScore);Assert.Null(p.EquipmentCount);Assert.Null(p.DocumentCount);
        Assert.Equal("Created",Assert.Single(f.Repository.History).Kind);
    }
    [Fact]
    public async Task Current_selection_is_per_user_idempotent_and_does_not_modify_resource_version()
    {
        using var f=new PropertyFactory();using var c=f.Client();var first=await Create(c);var second=await Create(c);
        Assert.False(second.IsCurrent);
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.OK,(await c.PutAsync($"/api/properties/{second.PropertyId}/current",null)).StatusCode);
        var current=await c.GetFromJsonAsync<PropertyDto>("/api/properties/current",Json);
        Assert.Equal(second.PropertyId,current!.PropertyId);Assert.Equal(1,current.Version);
        var page=await c.GetFromJsonAsync<PropertyPage>("/api/properties?pageSize=1",Json);
        Assert.Equal(2,page!.TotalCount);Assert.Equal(2,page.TotalPages);Assert.Equal(second.PropertyId,Assert.Single(page.Items).PropertyId);
        Assert.False((await c.GetFromJsonAsync<PropertyDto>($"/api/properties/{first.PropertyId}",Json))!.IsCurrent);
        using var other=f.Client(userId:Guid.NewGuid());Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api/properties/current")).StatusCode);
    }
    [Fact]
    public async Task Full_update_increments_version_and_audits_declared_role_changes()
    {
        using var f=new PropertyFactory();using var c=f.Client();var p=await Create(c);
        var response=await c.PutAsJsonAsync($"/api/properties/{p.PropertyId}",Update(p),Json);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var updated=(await response.Content.ReadFromJsonAsync<PropertyDto>(Json))!;
        Assert.Equal(2,updated.Version);Assert.Equal(4,updated.Bedrooms);Assert.Equal(OwnershipRole.Tenant,updated.OwnershipRole);Assert.True(updated.IsCurrent);
        Assert.Equal("RoleChanged",f.Repository.History.Last().Kind);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync($"/api/properties/{p.PropertyId}",Update(p),Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await c.DeleteAsync($"/api/properties/{p.PropertyId}?version=1")).StatusCode);
        Assert.True(f.Repository.Records[p.PropertyId].IsActive);
    }
    [Fact]
    public async Task Archive_preserves_record_and_history_and_selects_another_current_property()
    {
        using var f=new PropertyFactory();using var c=f.Client();var first=await Create(c);var second=await Create(c);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/properties/{first.PropertyId}?version=1")).StatusCode);
        Assert.False(f.Repository.Records[first.PropertyId].IsActive);Assert.Equal(2,f.Repository.Records[first.PropertyId].Version);
        Assert.Contains(f.Repository.History,x=>x.Property==first.PropertyId&&x.Kind=="PropertyArchived");
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/api/properties/{first.PropertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.PutAsync($"/api/properties/{first.PropertyId}/current",null)).StatusCode);
        Assert.Equal(second.PropertyId,(await c.GetFromJsonAsync<PropertyDto>("/api/properties/current",Json))!.PropertyId);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/properties/{second.PropertyId}?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/api/properties/current")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<PropertyPage>("/api/properties",Json))!.Items);
    }
    [Fact]
    public async Task Another_user_cannot_read_edit_archive_or_select_a_property_even_as_declared_owner()
    {
        using var f=new PropertyFactory();using var a=f.Client();var p=await Create(a);
        var otherUser=Guid.NewGuid();using var b=f.Client(userId:otherUser);
        Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/api/properties/{p.PropertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await b.PutAsJsonAsync($"/api/properties/{p.PropertyId}",Update(p,OwnershipRole.Owner),Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await b.DeleteAsync($"/api/properties/{p.PropertyId}?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await b.PutAsync($"/api/properties/{p.PropertyId}/current",null)).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<PropertyPage>("/api/properties?userId="+f.UserId,Json))!.Items);
        var own=await Create(b);Assert.NotEqual(p.PropertyId,own.PropertyId);
        Assert.Equal(f.UserId,f.Repository.Records[p.PropertyId].CreatedByUserId);
        // Future explicitly granted read membership must not confer creator write permissions.
        f.Repository.Members[(p.PropertyId,otherUser)]=OwnershipRole.Owner;
        Assert.Equal(HttpStatusCode.OK,(await b.GetAsync($"/api/properties/{p.PropertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await b.PutAsJsonAsync($"/api/properties/{p.PropertyId}",Update(p,OwnershipRole.Owner),Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await b.DeleteAsync($"/api/properties/{p.PropertyId}?version=1")).StatusCode);
    }
    [Theory]
    [InlineData("Owner")][InlineData("Tenant")][InlineData("Landlord")][InlineData("PropertyManager")]
    public async Task All_four_self_declared_roles_can_register_their_own_property(string role)
    {
        using var f=new PropertyFactory();using var c=f.Client();var p=await Create(c,PropertyFactory.Valid() with { OwnershipRole=Enum.Parse<OwnershipRole>(role) });
        Assert.Equal(role,p.OwnershipRole.ToString());
    }
    [Theory]
    [InlineData("addressLine", "x")][InlineData("city", " ")][InlineData("countryCode", "UK")]
    [InlineData("postcode", "invalid")][InlineData("propertyType", "Castle")][InlineData("ownershipRole", "Administrator")]
    [InlineData("bedrooms", "-1")][InlineData("bathrooms", "101")][InlineData("yearBuilt", "9999")]
    [InlineData("latitude", "91")][InlineData("longitude", "181")][InlineData("longitude", "1.12345678")]
    public async Task Invalid_values_return_validation_errors_without_creating_records(string field,string value)
    {
        using var f=new PropertyFactory();using var c=f.Client();
        var body=JsonSerializer.SerializeToNode(PropertyFactory.Valid(),Json)!.AsObject();
        body[field]=field is "bedrooms" or "bathrooms" or "yearBuilt" or "latitude" or "longitude" ? JsonNode.Parse(value) : JsonValue.Create(value);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/properties",body)).StatusCode);Assert.Empty(f.Repository.Records);
    }
    [Theory]
    [InlineData("propertyType")][InlineData("ownershipRole")][InlineData("bedrooms")][InlineData("bathrooms")]
    public async Task Missing_required_choice_or_count_is_not_silently_defaulted(string field)
    {
        using var f=new PropertyFactory();using var c=f.Client();var body=JsonSerializer.SerializeToNode(PropertyFactory.Valid(),Json)!.AsObject();body.Remove(field);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/properties",body)).StatusCode);Assert.Empty(f.Repository.Records);
    }
    [Theory]
    [InlineData("userId")][InlineData("isCurrent")][InlineData("healthScore")][InlineData("createdByUserId")]
    public async Task Server_owned_fields_are_rejected(string field)
    {
        using var f=new PropertyFactory();using var c=f.Client();var body=JsonSerializer.SerializeToNode(PropertyFactory.Valid(),Json)!.AsObject();body[field]="injected";
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/properties",body)).StatusCode);Assert.Empty(f.Repository.Records);
    }
    [Fact]
    public async Task Optional_year_and_coordinates_can_be_unknown_and_studios_can_have_zero_bedrooms()
    {
        using var f=new PropertyFactory();using var c=f.Client();
        var p=await Create(c,PropertyFactory.Valid() with { YearBuilt=null,Latitude=null,Longitude=null,Bedrooms=0 });
        Assert.Null(p.YearBuilt);Assert.Null(p.Latitude);Assert.Equal(0,p.Bedrooms);
        var bad=PropertyFactory.Valid() with { Longitude=null };
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/properties",bad,Json)).StatusCode);
    }
    [Theory]
    [InlineData("pageNumber=0")][InlineData("pageSize=101")][InlineData("pageSize=0")][InlineData("pageNumber=2147483648")]
    public async Task Invalid_pagination_is_rejected(string query)
    {
        using var f=new PropertyFactory();using var c=f.Client();Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/api/properties?"+query)).StatusCode);
    }
    [Fact]
    public async Task Delete_requires_positive_version_and_list_supports_large_safe_offsets()
    {
        using var f=new PropertyFactory();using var c=f.Client();var p=await Create(c);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.DeleteAsync($"/api/properties/{p.PropertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/properties?pageNumber=2147483647&pageSize=100")).StatusCode);
    }
    [Fact]
    public async Task Options_return_figma_choices_and_ISO_countries()
    {
        using var f=new PropertyFactory();using var c=f.Client();var options=await c.GetFromJsonAsync<PropertyOptions>("/api/property-options",Json);
        Assert.Equal(6,options!.PropertyTypes.Count);Assert.Equal(4,options.OwnershipRoles.Count);
        Assert.Contains(options.PropertyTypes,x=>x.Value=="FlatApartment"&&x.Label=="Flat / Apartment");
        Assert.Contains(options.Countries,x=>x.Code=="GB");Assert.Contains(options.Countries,x=>x.Code=="IR");
    }
    [Fact]
    public async Task Authentication_validates_signature_claims_and_live_identity_session()
    {
        using var f=new PropertyFactory();
        using var anonymous=f.Client(false);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/properties")).StatusCode);
        using var wrong=f.Client(signingKey:PropertyFactory.Key+"wrong");Assert.Equal(HttpStatusCode.Unauthorized,(await wrong.GetAsync("/api/properties")).StatusCode);
        using var missing=f.Client(sessionClaim:false);Assert.Equal(HttpStatusCode.Unauthorized,(await missing.GetAsync("/api/properties")).StatusCode);
        using var c=f.Client();f.Validator.Active=false;Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/properties")).StatusCode);
        f.Validator.Active=true;f.Validator.Unavailable=true;Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.GetAsync("/api/properties")).StatusCode);
        Assert.Empty(f.Repository.Records);
    }
    [Fact]
    public async Task Swagger_documents_CRUD_options_and_current_property()
    {
        using var f=new PropertyFactory();using var c=f.Client(false);using var doc=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));
        var paths=doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/properties").TryGetProperty("post",out _));
        var item=paths.GetProperty("/api/properties/{id}");Assert.True(item.TryGetProperty("get",out _));Assert.True(item.TryGetProperty("put",out _));Assert.True(item.TryGetProperty("delete",out _));
        Assert.True(paths.TryGetProperty("/api/properties/current",out _));Assert.True(paths.TryGetProperty("/api/properties/{id}/current",out _));Assert.True(paths.TryGetProperty("/api/property-options",out _));
    }
}
