# Zumaris

Monorepo for Zumaris microservices targeting .NET 8 and MySQL, using stored procedures and direct SQL without an ORM.

## Open in Visual Studio

Install Visual Studio 2022 version 17.8 or later with the .NET 8 SDK and the ASP.NET and web development workload, then open `Zumaris.sln`.

The solution contains Identity, Service Request, YARP Gateway, a shared response library, and ten four-layer service scaffolds. See [service ownership and scaffold setup](docs/service-architecture.md) for the complete inventory and development ports. Each service has its own solution folders. To run Identity alone, right-click `Identity.Api` and select **Set as Startup Project**. Select the `http` launch profile and press F5 to open Swagger.

Before using login/session features, manually run [database/identity/001_auth_sessions.sql](database/identity/001_auth_sessions.sql) in the existing Identity database. See [JWT instructions](docs/jwt.md) for the client changes, session endpoints, and deployment order. This script is not run automatically.

## Layout

- `src/Services/Identity`: Api, Application, Domain, Infrastructure.
- `src/Services/ServiceRequest`: Api, Application, Domain, Infrastructure; authenticated request creation.
- `src/BuildingBlocks/Zumaris.BuildingBlocks.Common`: shared response types.
- `src/Gateways/YarpGateway`: independent YARP API gateway.
- `tests/Identity.Tests`: Identity HTTP/JWT integration tests using in-memory persistence.
- `tests/ServiceRequest.Tests`: request creation, validation, retry and session-validation tests.
- `database/identity`: SQL scripts for manual execution.

## Build and run Identity

```sh
dotnet build Zumaris.sln
dotnet run --project src/Services/Identity/Identity.Api/Identity.Api.csproj --launch-profile http
```

The HTTP profile uses port 5105 and the Development environment. Existing application configuration is retained from the supplied archive. Optional process overrides use `ConnectionStrings__DefaultConnection` and `Jwt__Key`.

See [Identity integration notes](docs/identity.md) for validation results and database requirements.

## Run Gateway and Identity together in Visual Studio

Right-click the solution, open **Properties → Startup Project**, choose **Multiple startup projects**, and set both `Identity.Api` and `YarpGateway` to **Start**. Use the `http` profile for each project. Gateway listens on port 5039 and Identity on port 5105.

From a second terminal, start the Gateway with:

```sh
dotnet run --project src/Gateways/YarpGateway/YarpGateway.csproj --launch-profile http
```

See [Gateway integration notes](docs/gateway.md) for routes, validation, and remaining authentication integration work.

For the Windows server with Identity on port 5001, see [Gateway on IIS](docs/iis-gateway.md).

## Service Request

Follow [the Service Request guide](docs/service-request.md). First run `./scripts/Initialize-ServiceRequestLocalConfig.ps1` in PowerShell to create git-ignored local settings from Identity without changing credential values. Manually run [its separate schema script](database/service-request/001_service_requests.sql) and [lifecycle migration](database/service-request/002_request_lifecycle.sql) on MySQL, grant the application account access, and publish the updated Identity before starting the new service. On IIS the service uses internal port 5002 behind the existing `api.zumaris.com:443` Gateway; Development uses port 5106. Creation, listing, details, update, cancellation, timeline, photo upload and owner-only photo download are implemented. See [photo setup and remaining endpoints](docs/service-request-photos.md) before deploying.

Database changes will be supplied as SQL scripts for manual execution.
