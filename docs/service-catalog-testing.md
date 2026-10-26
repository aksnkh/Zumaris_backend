# Temporary frontend token for ServiceCatalog

Only the authenticated Area and Category lists in ServiceCatalog support this mode. Property, ServiceRequest and Identity are unchanged. It is disabled by default, but can be explicitly enabled in any environment, including Production. JWT validation and active-session checking remain in effect for real tokens. Icons remain public as before.

Publish ServiceCatalog.Api. In its server-side appsettings.Local.json, add this section at the JSON root, preserving existing settings:

```json
"Testing": {
  "AllowSimpleToken": true
}
```

Alternatively set the ServiceCatalog process environment variable `Testing__AllowSimpleToken=true`. Environment variables override JSON. Recycle only the ServiceCatalog application pool. Leave ASPNETCORE_ENVIRONMENT unchanged; Production is supported. No SQL or Gateway changes are required.

In the Service Catalog Swagger Authorize dialog, type `1` only. Swagger sends `Authorization: Bearer 1`. Frontend requests can send that same header. No cookie or BFF key is needed for this temporary catalog identity. Missing headers, other short tokens, or a disabled setting return 401. The lists still read the real configured MySQL database. A real signed JWT still requires an active Identity session.

Anyone with access to the API and the value `1` can read these two catalog lists while this switch is enabled. It is an authentication shortcut, not a valid JWT or normal session. Do not use it for Identity, Property or ServiceRequest.

After testing, set AllowSimpleToken to false (and remove any true environment override), recycle the ServiceCatalog pool and verify Bearer 1 returns 401. Preserve server-local settings during republish.

Run `dotnet test tests/ServiceCatalog.Tests/ServiceCatalog.Tests.csproj` for enabled/disabled behavior in all environments, missing/wrong tokens and real signed JWTs with active/inactive Identity sessions.
