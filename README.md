# Zumaris

Monorepo for Zumaris microservices targeting .NET 8 and MySQL, using stored procedures and direct SQL without an ORM.

## Open in Visual Studio

Install Visual Studio 2022 version 17.8 or later with the .NET 8 SDK and the ASP.NET and web development workload, then open `Zumaris.sln`.

The solution contains the Identity microservice and its shared response library. In Solution Explorer, right-click `Identity.Api` and select **Set as Startup Project**. Select the `http` launch profile and press F5 to open Swagger.

## Layout

- `src/Services/Identity`: Api, Application, Domain, Infrastructure.
- `src/BuildingBlocks/Zumaris.BuildingBlocks.Common`: shared response types.
- `src/Gateways/YarpGateway`: independent YARP API gateway.

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

Database changes will be supplied as SQL scripts for manual execution.
