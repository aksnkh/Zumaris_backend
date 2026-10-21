# Service ownership

Use `docs/service-architecture.md` and `scripts/service-scaffolds.json` to choose the owning service before adding a feature. All services belong to the existing Zumaris.sln; preserve its src/Services/<Service> solution-folder grouping.

- Identity: login, external identity, sessions and platform-wide roles.
- Property: property identity/address, user-property membership and its history; Home Passport composition and property-wide timeline.
- Document: property documents, certificate/warranty files, versions and expiry metadata.
- Maintenance: equipment, maintenance plans and due work.
- ServiceRequest: service orders, order photos, execution status, quotations, scheduling and request history.
- ServiceCatalog: categories, service definitions and area-type options.
- Provider: professional/company profile, credentials, coverage, portfolio, reviews and trust.
- Billing: plans, subscriptions, authoritative payments/refunds, financial invoices and commissions.
- Notification: delivery templates/preferences and sending notifications; other domains own their scheduling intent.
- PropertyIntelligence: health scores, AI extraction/recommendations, Home DNA/HomeGraph/Digital Twin capabilities.
- WebsiteBuilder: professional sites, templates/content, publishing and domains.
- Integration: external enterprise/partner adapters.

Do not create separate Admin, HomePassport, History or Booking services for features already owned above. Administrative controllers belong to the owning service. Provider owns reviews. ServiceRequest owns its photos.

Use .NET 8 and Clean Architecture: Domain has no outer-layer dependencies; Application references Domain; Infrastructure implements Application contracts; Api composes Application/Infrastructure. Do not introduce cross-service project references, ORM usage or queries/joins into another service's schema. Use parameterized MySQL queries or stored procedures and provide manual SQL scripts under database/<service-route>. Do not execute SQL against the user's database unless explicitly requested for that task.

Property now has authenticated CRUD/current-selection/options, a manual database script and an active Gateway route; see docs/property.md. Nine services remain scaffolds without business endpoints, Identity authentication wiring, database schemas or Gateway routes. Add JWT/session validation, resource authorization and relevant verification with their first business endpoints before deployment. `/health` is process liveness only. Never treat scaffold existence as completed product functionality.

For Property, user-declared Owner/Tenant/Landlord/PropertyManager membership does not imply an Identity administrator role or permission to claim another user's existing property. Preserve the user's current self-declaration requirement without requiring verification workflows unless requested.
