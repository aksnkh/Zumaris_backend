# Application-scoped authentication

Identity issues a separate session for each application. A registered Identity account is the existing base customer account (`App`); an active, consented Provider signup additionally permits `Tech`. An active `platform_administrators` membership permits `Admin`. Choosing an application never grants membership.

## Login

`POST /identity/api/auth/login`

```json
{
  "userName": "user@example.com",
  "password": "your-password",
  "application": "Tech"
}
```

`application` is mandatory and case-sensitive: `App`, `Tech` or `Admin`. Missing/invalid values return 400. Invalid credentials return 401. Correct credentials without the destination application's membership return 403 with no new access token, refresh token or session. Failed Provider reference validation returns 503, never a Tech grant. App login does not depend on Provider availability.

The existing `Response<AuthResponse>` payload now additionally includes `application`, `role` and nullable `providerId`. JWT claims include `sub`, `sid`, `application`, `role`, and (Tech only) `providerId`. Role is the active application's role: `Customer`, `Provider`, or `Admin`, respectively. Client-supplied role/providerId values cannot grant access. A token has exactly one active application context, even when the account belongs to multiple applications.

Tech membership requires an existing active Provider profile **and** `provider_signup_details`, not merely a draft row created through the old profile API. Verified documents are not a login requirement; unverified technicians must sign in to upload documents. Document/qualification checks continue to govern opportunities and quoting.

## Existing-customer technician signup

1. Sign in to Identity with `application: "App"`, or use the App token returned by the existing Identity signup endpoint.
2. Complete personal details if needed.
3. Call `POST /provider/api/providers/signup` with the App Bearer token and `{ "acceptTermsAndPrivacy": true }`.
4. Sign in again using the same account credentials and `application: "Tech"` to obtain a Tech session/token.
5. Use that Tech token for the provider profile, documents and technician work APIs.

Provider signup is an explicit enrolment exception, accessible to App and Tech. It neither upgrades the App token nor creates a duplicate Identity account. App tokens cannot read/edit Provider private data through `me`, create quotes, or execute technician work. The existing Identity `/api/auth/signup` remains base account creation and issues an App token; it does not issue Tech/Admin credentials.

## Refresh, logout and simultaneous applications

Browser requests retain `X-Zumaris-CSRF: 1` and use credentials for refresh/logout:

```http
POST /identity/api/auth/refresh?application=Tech
POST /identity/api/auth/logout?application=Tech
```

The query application is mandatory. Each application has its own HttpOnly refresh cookie: `zumaris_refresh_token_app`, `zumaris_refresh_token_tech`, or `zumaris_refresh_token_admin`. Secure cookies remain enabled outside Development; existing SameSite/CSRF protections remain. App and Tech login therefore do not overwrite each other's refresh cookie.

The application and providerId are stored in Identity's session row. Refresh reuses that context, rechecks destination membership, rotates the token once, and preserves absolute session expiry. Putting an App refresh token into a Tech refresh request returns 403; it cannot become Tech. Removing membership revokes that application's session on refresh. An unavailable reference service returns 503 without granting access or rotating the refresh token. When a Bearer token is also supplied, it must target the same application as refresh/logout.

Identity JWT/session validation also compares the token's application/providerId to the stored session. Legacy sessions without context are not accepted. Session listing/revocation under `/api/auth/sessions` is restricted to the active application's sessions, including delete-all.

The existing BFF Google sign-in `/identity/api/auth/google` now also requires `application` next to `idToken` and applies the same server membership checks. Google linking remains an account operation with its existing request. BFF refresh/logout bodies must include `application` next to `refreshToken`; these routes still require the server-only BFF key. No BFF secret is added to browser code.

## API access boundaries

Validation runs in each API service, independently of Gateway/CORS/Origin. Signed application, role and (for Tech) providerId are checked before MVC model binding and business actions. Wrong application context returns HTTP 403 with `Response<T>`-compatible error data. JWT authentication, active-session validation, ownership and administrator checks continue to apply; the application check does not replace them. Tokens missing application/role context cannot use protected business APIs.

| Resource | Accepted application |
|---|---|
| Property CRUD/options, customer service requests/history, payments | App |
| Provider own profile/eligibility/private documents | Tech |
| Independent document administration and old Provider admin routes | Admin, plus server-side administrator membership |
| Technician opportunities/jobs/start/complete | Tech |
| Quote context, submit, sent list, withdraw | Tech |
| Customer quote list, accept, decline | App |
| Quote detail and job photo download | App or Tech, with role-specific record ownership |
| Customer execution/completed-work context, customer review submission | App |
| Provider signup | App or Tech, explicit enrolment only |
| Provider public summary/quote profile/reviews, completed-job counts | App, Tech or Admin; public business data only |
| Category/area lookup | App, Tech or Admin |
| Identity own profile and session validation | App, Tech or Admin, current user/session only |
| Identity admin-access | Admin plus active server-side administrator membership |

An account's Tech token cannot use its customer identity to read a received quote or customer photo, and its App token cannot use its provider identity to read sent quotes or provider-only photos. Repository predicates distinguish both sides even when both roles belong to the same Identity user. Shared resources remain intentionally shared and retain resource authorization.

ServiceCatalog's pre-existing explicitly enabled simple test token remains limited to catalog lookups and is assigned an App lookup context there. It is not an Identity JWT and grants no access to Provider/ServiceRequest or other services. Public icon routes remain public.

## Provider login-access integration

Identity calls `GET /api/internal/users/{userId}/login-access` on Provider. The internal controller uses a separate JWT authentication scheme and audience `Zumaris.Provider.LoginAccess`, scope `provider-login-access`, and a maximum 30-second credential lifetime. The signed subject must equal the path userId. Ordinary App/Tech/Admin access tokens are rejected there; the internal credential cannot access user business APIs. It is created server-side and never returned to the frontend. Both services continue to use their configured matching Identity signing key and issuer; no cross-service SQL/project references or additional browser secret are introduced.

Set Identity's `Provider:BaseUrl` if needed. Default IIS address: `http://127.0.0.1:5007/`; Development Provider generally uses port 5111. Only HTTPS or loopback HTTP, no URL credentials, trailing slash required. Requests use a five-second timeout and do not follow redirects. Provider does not call back to Identity for this dedicated internal lookup, avoiding a login/session-validation cycle.

## Manual database migration and deployment

Run **`database/identity/006_application_sessions.sql`** in **zumaris_identity** after existing Identity scripts. The script adds application/provider_id to sessions, is repeatable, and revokes old sessions without a known application. Accounts, passwords and Provider data are unchanged. Users must sign in again. This script is for a SQL client; it is not executed by application startup. Provider's already-required schema 003 (consent) and 004 (documents) must already exist; no new Provider DDL is needed for this change.

Coordinate frontend login/refresh updates with deployment. Redeploy **Identity, Provider, ServiceRequest, Property, Billing and ServiceCatalog** because each service enforces its own boundary through the common building block. Gateway routing/CORS need no changes. Do not deploy Identity alone and leave other services on old authorization code. Runtime database privileges remain SELECT/INSERT/UPDATE, without DDL.

The new Identity migration is exercised only against a disposable localhost test schema; no SQL is executed against the user's databases.
