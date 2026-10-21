# WebsiteBuilder

Scaffold only: .NET 8 / Clean Architecture. No business endpoints, authentication wiring or persistence implementation yet.

- Domain: business entities and rules; no dependencies on outer layers.
- Application: use cases and repository/integration contracts; references Domain.
- Infrastructure: future MySQL stored procedures/direct SQL and integrations; references Application and Domain. No ORM.
- Api: HTTP composition root; references Application and Infrastructure.
- Development: http://localhost:5115/swagger
- Liveness: GET /health (process check only, not business/database readiness).
- Planned Gateway prefix: /website-builder (not registered until implementation).
- Planned MySQL schema: zumaris_website_builder (not created).
- Suggested future IIS HTTP port: 5011 (not configured).

No database scripts run automatically. Add manual SQL migrations under database/website-builder.
Configure Identity JWT/session validation and resource permissions before exposing business endpoints.
See docs/service-architecture.md for ownership and cross-service boundaries.
