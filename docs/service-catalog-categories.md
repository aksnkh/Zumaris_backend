# Work categories

ServiceCatalog owns work categories. `GET /service-catalog/api/categories` returns records ordered by `id`, each containing `id` (category code), `name` (description) and `iconUrl`. The list requires a valid Zumaris Bearer token and active Identity session. There are no category create/update/delete endpoints. ServiceRequest inputs and storage are unchanged by this feature.

```json
[
  {
    "id": 1,
    "name": "Plumbing",
    "iconUrl": "/service-catalog/api/categories/icons/1.png"
  }
]
```

## Manual SQL and IIS

1. Run `database/service-catalog/003_categories.sql` manually as root/admin. It creates `zumaris_service_catalog.categories` and seeds the 17 agreed IDs/names with filenames `1.png`–`17.png`. It never runs automatically. An unchanged rerun preserves existing records; inspect the final SELECT for conflicts if data already exists. The runtime account requires SELECT on this table.
2. Place the PNGs in `C:\ZumarisData\CategoryIcon`. Give `IIS AppPool\<actual ServiceCatalog pool name>` Read & Execute permission. The API does not create icons or upload files. Directory and public URL prefix are configurable through `CategoryIcons:Directory` and `CategoryIcons:PublicPath`.
3. Publish ServiceCatalog.Api with its IISProduction profile, retain server `appsettings.Local.json`, then recycle its pool. Existing Identity JWT/session settings and ServiceCatalog port 5006 are reused. The existing Gateway catch-all already forwards these requests; no Gateway republish is needed for this addition.
4. Select Service Catalog Service in Gateway Swagger and call GET `/service-catalog/api/categories` with a valid token. Expect all 17 categories. Open `https://api.zumaris.com/service-catalog/api/categories/icons/1.png` to check image delivery. Icons are public so `<img>` can load them; the list remains authenticated. Frontends on another origin must resolve `iconUrl` against the API origin, e.g. `new URL(category.iconUrl, "https://api.zumaris.com").href`.

Missing or unsupported icon files return 404. No Windows filesystem paths are exposed. Existing Area icon delivery is unchanged. Supported file extensions and path restrictions use the same policy as Area icons.

## Category inventory and subservice examples

Examples below describe intended scope only; they are not separate persisted services or endpoints in this change. Gas Safe eligibility is not implemented by this lookup list and must be enforced when gas work allocation is implemented.

| ID | Category | Subservice examples |
| --- | --- | --- |
| 1 | Plumbing | Leak, blocked drain, tap, toilet, shower, stopcock |
| 2 | Electrical | Socket, lighting, consumer unit, fuse, EV charger |
| 3 | Heating | Boiler, radiator, thermostat, no heating/hot water |
| 4 | Gas | Gas safety, cooker connection (Gas Safe only) |
| 5 | Drainage | Blocked drain, gutter, soil stack |
| 6 | Appliances | Washing machine, dishwasher, oven, fridge |
| 7 | Carpentry / Handyman | Doors, shelves, furniture assembly, minor fixes |
| 8 | Painting & Decorating | Walls, ceilings, exterior touch-up |
| 9 | Roofing | Leak, tiles, flat roof, guttering related |
| 10 | Locksmith | Lock change, boarded door, stuck lock |
| 11 | Glazing / Windows | Broken glass, window repair, seal |
| 12 | Pest Control | Mice, insects, wasps |
| 13 | Cleaning | End of tenancy, deep clean, after-build |
| 14 | Landscaping / Garden | Garden maintenance, fencing, patio |
| 15 | Insulation & Ventilation | Extractor fan, loft insulation, damp-related |
| 16 | Smart Home | Thermostat install, doorbell, cameras |
| 17 | Other | Free-text / Ops routes manually |

## Validation

`dotnet test tests/ServiceCatalog.Tests/ServiceCatalog.Tests.csproj` checks category and area GET contracts, authentication requirements, absence of write routes, public image bytes/content type, missing files and invalid filenames. Repository substitutions are used for HTTP tests; perform the SQL and authenticated 17-row GET checks on the target environment after deployment.
