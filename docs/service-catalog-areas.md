# Property areas — read-only ServiceCatalog API

`GET /service-catalog/api/areas` through Gateway returns an array of `{ "id": 1, "name": "Kitchen" }`, ordered by ID. A valid Zumaris Bearer JWT and active Identity session are required. No BFF key is needed. No POST, PUT or DELETE endpoints exist. Data is read from MySQL, not hard-coded in the API. IDs 1–19 and labels are defined in `database/service-catalog/001_areas.sql`.

## Manual database setup

Run `database/service-catalog/001_areas.sql` manually on MySQL 8. It creates `zumaris_service_catalog.areas` and seeds the 19 agreed values. Reruns preserve existing rows; compare the final SELECT output against the script if the schema already contains data. The script never runs at startup. Use a runtime database account with SELECT only on this table; use a separate administrative account to run the script. No credentials or automatic GRANT statements are supplied.

## IIS deployment

1. Publish ServiceCatalog.Api and YarpGateway with their `IISProduction` profiles. Install the .NET 8 Hosting Bundle on the Windows server.
2. Create a separate ServiceCatalog site and No Managed Code application pool. Bind HTTP to `127.0.0.1:5006`, hostname empty. Grant its pool identity Read & Execute on its publish directory.
3. Run `scripts/Initialize-ServiceCatalogLocalConfig.ps1 -IdentitySettingsPath <actual Identity appsettings.json> -OutputDirectory <actual ServiceCatalog publish directory>`. It creates an ignored `appsettings.Local.json`, preserving existing files. It copies JWT and connection settings, changing only the database to `zumaris_service_catalog`. If Identity uses environment overrides, use its effective settings instead. Change ServiceCatalog's database user to the SELECT-only account. Never commit credentials.
4. Production `Identity:BaseUrl` defaults to `http://127.0.0.1:5001/`; Identity must include the session-validation endpoint. JWT key, issuer and audience must match Identity. Development uses Identity port 5105 and ServiceCatalog port 5110.
5. Preserve server local settings on future deployments. Restart the service pool. Update Gateway; Production destination is 5006, Development is 5110.
6. On the server, verify `http://127.0.0.1:5006/swagger/v1/swagger.json`, then public `/service-catalog/swagger/v1/swagger.json`. Select Service Catalog Service in Gateway Swagger. GET `/service-catalog/api/areas` without a token must return 401; with a valid active token it must return the 19 records.

`/health` is process liveness only, not database readiness. MySQL failures return a generic server error; inspect server logs without exposing credentials. Identity session validation failures return 503; invalid tokens/sessions return 401.

## Scope and validation

This change supplies the reference list only. ServiceRequest input/storage/area validation has not changed. Linking orders to an area requires a separate change using the ServiceCatalog API, never direct cross-schema SQL.

Run `dotnet test tests/ServiceCatalog.Tests/ServiceCatalog.Tests.csproj` for the HTTP read-only contract using a substituted repository and test authentication. These tests do not establish live MySQL connectivity or IIS readiness. Run the authenticated 19-row GET after manually applying the script on the target server.

## Area icons

Manually run `database/service-catalog/002_area_icons.sql` after `001_areas.sql`, including on new installations. It adds nullable `areas.icon` and sets the agreed mappings for IDs 1–19 to `1.png` through `19.png`. Rerunning resets those mappings to the same convention. Place the matching PNG files in the configured directory; the SQL does not create files. Unmapped areas return `iconUrl: null`.

Place administrator-managed image files in `C:\ZumarisData\AreaIcon` and grant the ServiceCatalog application pool Read & Execute on this folder. The application does not create the folder or upload files. Configure `AreaIcons:Directory` to change the absolute directory. Keep this directory limited to public icons and avoid symbolic links. Supported extensions: png, jpg, jpeg, webp, svg, ico. Unsupported/missing files return 404; only simple filenames are accepted.

GET `/service-catalog/api/areas` returns `{ "id": 1, "name": "Kitchen", "iconUrl": "/service-catalog/api/areas/icons/1.png" }` when mapped. The URL is relative to the API origin, not the frontend origin. With a separate frontend host, resolve it against `https://api.zumaris.com`. The Windows disk path is never returned. `AreaIcons:PublicPath` defaults to `/service-catalog/api/areas/icons` and can be changed if the Gateway prefix changes.

Icon GETs are public so browser image tags need no Bearer header; the area list still requires authentication. The existing Gateway route already forwards icon requests; no Gateway change is needed for this addition. Publish the updated ServiceCatalog.Api after applying the migration. Check the authenticated list and a mapped icon URL after deployment. The cloud tests use a temporary directory and substituted database; real Windows ACLs and server MySQL remain to be verified by the deployer.
