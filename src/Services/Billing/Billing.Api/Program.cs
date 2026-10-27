using Zumaris.BuildingBlocks.Common.Validation;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddValidationMessages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Billing API (Scaffold)", Version = "v1" });
    options.AddServer(new OpenApiServer { Url = "../.." });
});
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("v1/swagger.json", "Billing API (Scaffold)"));
app.UseCors();
// Liveness only: there is no database, business workflow or authentication integration yet.
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
