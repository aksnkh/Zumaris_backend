using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.BuildingBlocks.Common.Validation;
using Zumaris.Document.Application;
using Zumaris.Document.Infrastructure;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=22*1024*1024);
builder.Services.Configure<IISServerOptions>(o=>o.MaxRequestBodySize=22*1024*1024);
builder.Configuration.AddJsonFile("appsettings.Local.json",optional:true,reloadOnChange:false)
    .AddEnvironmentVariables().AddCommandLine(args);
builder.AddPassportSecurity();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IDocumentRepository,DocumentRepository>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddValidationMessages().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    // User identity, current selection and summary statistics are server-owned fields.
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear(); options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback); options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Document API", Version = "v1" });
    c.AddServer(new OpenApiServer { Url = "../.." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        { [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>() });
});
var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("v1/swagger.json", "Document API v1"));
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
public partial class Program;
