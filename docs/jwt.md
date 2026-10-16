# Identity JWT and sessions

## Apply the database script yourself

1. Select the existing Identity MySQL database (MySQL 8.0 or later), take your usual backup, and confirm the names `zumaris_auth_sessions` and `zumaris_auth_refresh_tokens` are not already used for another schema.
2. Run `database/identity/001_auth_sessions.sql` manually before deploying this version. An unchanged rerun is safe; `IF NOT EXISTS` does not repair an incompatible existing table. Check the definitions with `SHOW CREATE TABLE` if either name already exists.
3. The application connection needs SELECT, INSERT and UPDATE on the two new tables, plus its existing procedure permissions. DDL creation needs the usual CREATE/REFERENCES privileges for the account running the script.
4. Deploy/restart Identity and Gateway, then sign in again. Old access tokens without `sid` and old refresh tokens are deliberately not migrated.

The script only adds two InnoDB tables and a foreign key between them. It does not touch existing users, password hashes, stored procedures, or connection settings. There is no user-table foreign key because the supplied source does not include the existing schema. User IDs remain the GUIDs returned by the current user lookup procedure.

**The script has not been executed by Codex.** Database-backed login/registration and the MySQL locking/rollback behavior must be validated after you run it. Automated HTTP tests replace database repositories with an in-memory contract fake; they do not prove MySQL transaction behavior.

## Token/session lifecycle

- Access tokens: HS256, issuer/audience/signature/algorithm/expiry validation, `sub`, `sid`, `jti`, `iat` claims, 15-minute lifetime by the existing configuration. Validation permits 15 seconds of clock skew, but session expiry is independently enforced in the database.
- Refresh tokens: 64 cryptographically random bytes encoded as Base64URL; only a SHA-256 hash is stored. The raw token is returned only in the HttpOnly cookie, never in the JSON payload or logs.
- Each login creates an independent session. Refresh rotates the opaque token exactly once under an InnoDB session-row lock and transaction. The session has an absolute seven-day lifetime by the existing configuration; refresh does not extend that deadline. Access expiry is capped by the session deadline.
- Token hashes remain until the session is cleaned up. Reusing a consumed token atomically revokes the entire session, including its latest refresh token. **Serialize refresh calls in the client**: simultaneous refreshes or retries using an old token intentionally trigger revocation. A lost refresh response requires signing in again.
- Every authenticated Identity request checks that `sid` belongs to `sub` and is still active in the database. Logout/revoke/reuse therefore invalidate access tokens on subsequent Identity requests. Requests already in flight cannot be undone.
- Refresh re-reads the current user through `sp_identity_get_user_by_username`, verifies the same user ID and active status, and includes current user details in the new access token. Disabling/renaming a user prevents the next refresh; for immediate access revocation, also revoke that user's sessions. Account-disable hooks in other services are outside this change.
- Existing `sp_identity_register_user` and `sp_identity_get_user_by_username` are retained. Legacy refresh procedures/tables are no longer used or modified.

## Client contract

All mutating auth requests require **`X-Zumaris-CSRF: 1`**, including login, registration, refresh, logout and session deletion. A custom header forces cross-origin browser callers through CORS preflight; do not allow untrusted origins. Invalid/missing header returns 400. Existing clients must add it.

| Endpoint on Identity | Purpose | Authentication |
| --- | --- | --- |
| `POST /api/auth/login` | Username/password login | Public + CSRF header |
| `POST /api/auth/register` | Register user; password minimum 8 characters | Public + CSRF header |
| `POST /api/auth/refresh` | Rotate refresh token | Refresh cookie + CSRF header |
| `POST /api/auth/logout` | Revoke current cookie session; also revoke bearer session when provided | Cookie or valid bearer + CSRF header; idempotent 204 even when absent |
| `GET /api/auth/sessions` | List this user's active sessions, with `isCurrent` | Bearer token |
| `DELETE /api/auth/sessions/{id}` | Revoke an owned session | Bearer + CSRF header |
| `DELETE /api/auth/sessions` | Revoke all owned sessions | Bearer + CSRF header |

Through Gateway, prepend `/identity` to each endpoint. Login/refresh retain the response envelope and `payload.accessToken`, `payload.expiresAtUtc`, `payload.userName`, adding `payload.refreshTokenExpiresAtUtc`. No raw refresh token is included. Logout now returns 204 without the old response envelope. Deleting an unknown or another user's session also returns 204 without revealing its existence.

Keep access tokens in client memory and send `Authorization: Bearer <token>`. Browser fetch calls using cookies should use `credentials: 'include'`. The host-only refresh cookie uses `Path=/`, HttpOnly and SameSite=Strict, plus Secure outside Development. Root path supports both direct Identity and Gateway paths; old path-scoped cookies are removed when setting/deleting it. Host Identity and Gateway on the same site; cross-site cookie use is intentionally unsupported. Cookies are shared across ports on the same host.

Swagger works directly and behind Gateway using a document-relative server URL. In **Authorize**, set CSRF to `1` and Bearer to the access token when testing protected routes. Refresh cookies are managed by the browser.

## Configuration and deployment

Existing connection strings and JWT signing values are unchanged. Startup validates that the signing key has at least 32 UTF-8 bytes, issuer/audience are set, access lifetime is 1–30 minutes, and absolute session lifetime is 1–30 days. A long string is not proof of a high-entropy key; use a random secret when replacing the development key.

Both applications now use an explicit CORS allowlist. Same-origin access (including Gateway Swagger) needs no CORS configuration. For a trusted, same-site frontend on a different origin, set `Cors:AllowedOrigins` in **both** applications, e.g. environment variable `Cors__AllowedOrigins__0`; use exact scheme/host/port origins, never `*` with credentials. No frontend origins are assumed.

Identity limits controller requests to 30 per minute per observed remote IP, per application instance. Behind Gateway, Identity sees the Gateway IP and those requests share a bucket. This conservative limit needs adjustment/distributed enforcement at the edge when scaling; forwarded IP headers are deliberately not trusted from arbitrary callers.

Exception responses are generic Problem Details; stack traces, SQL/connection details and password hashes are not returned to clients. Password console logging was removed. Existing password hashes/formats remain supported unchanged.

Gateway routes requests; it does not issue or independently validate JWTs. Future services must validate signature, issuer, audience, lifetime and algorithm themselves, and perform a shared session check/introspection if immediate session revocation is required there. Immediate access revocation in this version is verified for Identity only.

For routine cleanup, delete expired sessions only after a retention window; the foreign key removes their token history. Never delete consumed hashes while a session is live. The SQL file includes an optional commented maintenance command; it is not scheduled or executed automatically.

## Validation

Run:

```sh
dotnet build Zumaris.sln
dotnet test tests/Identity.Tests/Identity.Tests.csproj
```

The tests exercise the actual Identity HTTP/JWT middleware using fake repositories: issuance, hash-only storage contract, rotation, replay, concurrent refresh, absolute expiry, cookie-only logout, immediate revocation, session ownership, disabled users, invalid token signatures/issuer/audience/algorithm/expiry/claims, CSRF, CORS, validation, production Secure cookies, rate limits, and Swagger server resolution.

After applying SQL, manually verify against a disposable account: login → list sessions → refresh → replay the previous refresh token → check both latest refresh and bearer token are rejected. Also verify two simultaneous refreshes have one initial success followed by session revocation, single/all-session logout, and rollback on a failed insertion. Do not run this against a session you need to keep.
