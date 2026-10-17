var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 44 * 1024 * 1024);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = 44 * 1024 * 1024);

// ۱. رجیستر کردن YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ۲. اضافه کردن سرویس‌های سواگر
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ۳. تنظیم CORS
// Cross-origin browser access must be explicitly configured; same-origin Swagger needs no CORS.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
}));

var app = builder.Build();

app.UseCors();

// ۴. فعال‌سازی Swagger UI و تجمیع اندپوینت‌ها
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // مسیر JSON هر میکروسرویس از دل Gateway صدا زده می‌شود:
        options.SwaggerEndpoint("/identity/swagger/v1/swagger.json", "Identity Service");
        options.SwaggerEndpoint("/service-request/swagger/v1/swagger.json", "Service Request Service");
        //options.SwaggerEndpoint("/home-passport/swagger/v1/swagger.json", "Home Passport Service");
        //options.SwaggerEndpoint("/billing/swagger/v1/swagger.json", "Billing Service");
        //options.SwaggerEndpoint("/booking/swagger/v1/swagger.json", "Booking Service");
        //options.SwaggerEndpoint("/job-management/swagger/v1/swagger.json", "Job Management Service");
        //options.SwaggerEndpoint("/notification/swagger/v1/swagger.json", "Notification Service");
        //options.SwaggerEndpoint("/provider/swagger/v1/swagger.json", "Provider Service");

        // اگر می‌خواهید با باز کردن آدرس اصلی گیت‌وی (localhost:5000) مستقیماً سواگر باز شود:
        options.RoutePrefix = string.Empty;
    });
}

// ۵. فعال‌سازی Reverse Proxy
app.MapReverseProxy();

app.Run();
