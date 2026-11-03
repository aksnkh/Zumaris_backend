# Provider

Provider owns professional identity and category qualifications. Verified/active quote-profile reads now require Identity JWT/session validation and database-backed qualification checks. No provider self-verification or administrative write API exists.

- Development: port 5111; IIS internal port 5007.
- Gateway prefix: `/provider`.
- Schema: `zumaris_provider`; manually run `database/provider/001_provider_quote_profiles.sql`.
- `GET /api/providers/me/quote-profile?categoryId=1` and `GET /api/providers/{providerId}/quote-profile?categoryId=1` provide quote eligibility.
- `/health` is liveness only.

See [quote workflow, manual SQL and IIS setup](../../../docs/service-request-quotes.md). Other professional profile, review and verification-management capabilities remain separate work. Cross-service communication uses APIs, never cross-schema joins.
