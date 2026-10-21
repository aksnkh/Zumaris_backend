# Property

.NET 8 / Clean Architecture / direct parameterized MySQL, without an ORM.

Implemented: authenticated property CRUD, self-declared user-property roles, per-user current property selection and form options. Identity JWT and live session validation are required. Soft archive and optimistic version checks preserve history.

- Domain: property types, declaration roles and immutable records.
- Application: validation, use cases and repository contracts.
- Infrastructure: MySQL transactions and the Identity session HTTP client.
- Api: controllers, JWT/session authentication, CORS and Swagger.
- Development: HTTP 5107; Identity HTTP 5105.
- Production IIS: internal HTTP 5003; Identity HTTP 5001.
- Public Gateway prefix: `/property`.
- MySQL schema: `zumaris_property`; manually apply `database/property/001_properties.sql`.
- Configure existing Identity credentials/JWT with `scripts/Initialize-PropertyLocalConfig.ps1`; the local JSON is ignored by Git.

See [setup and API instructions](../../../docs/property.md) and [service ownership](../../../docs/service-architecture.md).
