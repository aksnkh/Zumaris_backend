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

Property now has authenticated CRUD/current-selection/options, a manual database script and an active Gateway route; see docs/property.md. ServiceCatalog has lookup APIs; Provider has verified professional/category eligibility for ServiceRequest quotes (docs/service-request-quotes.md). Provider now also owns onboarding/documents/admin verification and customer reviews. ServiceRequest owns technician opportunities and execution; Billing owns payment intents only (PayPal charging/settlement are deferred). See docs/technician-order-workflow.md. Six services remain scaffolds without business endpoints, Identity authentication wiring, database schemas or Gateway routes. Add JWT/session validation, resource authorization and relevant verification with their first business endpoints before deployment. `/health` is process liveness only. Never treat scaffold existence as completed product functionality.

For Property, user-declared Owner/Tenant/Landlord/PropertyManager membership does not imply an Identity administrator role or permission to claim another user's existing property. Preserve the user's current self-declaration requirement without requiring verification workflows unless requested.

The user-confirmed workflow in docs/order-workflow-confirmed.md governs technician work: identity/documents require administrator review; specialty categories are self-declared without separate admin approval. Only the assigned technician starts/completes work; completion requires report and result photo(s). Customer review is available immediately after completion, independent of payment. Payment is the accepted fixed quote after completion; only the high-level PayPal intent stage is implemented. Do not add settlement, tickets or detailed gateway flows without a later instruction.

Property CRUD uses last-write-wins without a version input, response field or version predicate. Preserve creator/member authorization, transactional locking, soft archive and membership history. The legacy database version column is unused; no schema migration is required.

The technician signup screen has no primary-trade field. Identity owns separate App/Tech/Admin account tables, including per-account personal data and credentials; Provider owns the Tech professional profile and consent. POST /api/tech/register requires consent and provisions Provider via a short-lived server credential, with retry on Tech login. It must never use an App account/token or merge accounts by email. See docs/independent-accounts.md and docs/technician-signup.md. Email password recovery remains deferred.

The approved global CORS origins for Gateway and service APIs are https://app.zumaris.com, https://tech.zumaris.com and https://admin.zumaris.com, with credentials. Use the shared/default policy for every route; do not restrict these origins to particular microservices. CORS does not replace resource or administrator authorization.

Final technician verification requirements (2026-11-07): IdentityCheck (one Passport/DrivingLicence/OtherId), Insurance and Certificate are independently administrator-reviewed and mandatory for every technician. Postcode is required profile information; OtherSkills is optional and does not replace self-declared category eligibility. Counts/percentages belong to the frontend. Keep the previous verified file active until a replacement is approved; rejecting a replacement does not revoke it. See docs/provider-verification-documents.md and docs/provider-document-review-decisions.md.

Application-scoped authentication uses independent app_accounts, tech_accounts and admin_accounts in Identity. Each has separate email/username uniqueness and account IDs. Register/login/refresh/logout use /api/app, /api/tech or /api/admin; the path fixes the application. JWT/session context and resource authorization remain enforced. Same email in another table never grants access or shares a profile. Google links are scoped by application; Tech/Admin Google must be explicitly linked after registration.

Administrator creation belongs to Identity and writes admin_accounts only. /api/admin/register requires an active Admin account/token, or the private server key for the one-time first-administrator setup. Legacy /api/admin/accounts and bootstrap remain protected aliases. Never auto-promote another application's same-email account, reopen bootstrap on account deactivation, or replace the creator's browser session with a target-user token.

The user explicitly authorized deleting account data rather than migrating. Identity reset 008 drops the old account schema and replaces it; scripts 001..007 are historical and must not be applied afterward. Provider data reset is separate in its own schema. Do not execute these destructive scripts on user databases without explicit execution instructions; integration tests use guarded disposable localhost schemas.
