# Password recovery — App, Tech and Admin

The user has enabled the previously deferred email-password-recovery scope for all three independent
account tables. Identity owns token issuance/password changes/session revocation. Notification owns
email construction and SMTP delivery; no cross-service project references or SQL are introduced.

## Frontend routes

All requests are POST JSON through the existing Identity Gateway route. No Bearer token or refresh
cookie is needed. Keep `X-Zumaris-CSRF: 1`, including on these anonymous endpoints. CORS remains the
approved app.zumaris.com, tech.zumaris.com and admin.zumaris.com origins with credentials.

| Side | Request link | Set new password |
|---|---|---|
| App | `/identity/api/app/forgot-password` | `/identity/api/app/reset-password` |
| Tech | `/identity/api/tech/forgot-password` | `/identity/api/tech/reset-password` |
| Admin | `/identity/api/admin/forgot-password` | `/identity/api/admin/reset-password` |

Forgot body:
```json
{"email":"person@example.com"}
```
A valid request always returns **202** and the same Response<T> envelope/message for eligible,
unknown, inactive, deleted, Google-only and account-cooldown cases:
```json
{
 "statusCode":202,"title":"Success","messageLevel":"Cif",
 "message":"If an eligible account exists, password reset instructions will be sent to its email address.",
 "payload":null
}
```
Do not use this result as proof of account existence or email delivery. No token/link/account data
is returned. Accounts with no local password use their existing Google sign-in; this flow does not
silently enable a new password login for those accounts.

Email opens the corresponding configured frontend page (defaults below), with the secret in the
**URL fragment**, e.g. `https://app.zumaris.com/reset-password#token=...`. The fragment is not sent
to the frontend web server in the HTTP URL. Frontend must read `window.location.hash`, extract the
`token`, remove the fragment from browser history and submit it in the reset POST body. Avoid putting
it in query strings, analytics/error reports, storage or application logs. Show an invalid/expired-link
message when token is missing; do not automatically submit/change the password on a page GET.

Reset body:
```json
{"token":"the-token-from-the-email-fragment","newPassword":"the-new-password","confirmPassword":"the-new-password"}
```
Use the endpoint matching that frontend's side. Passwords must match, contain 8..1024 characters,
and not be whitespace-only, consistent with existing signup policy. The token identifies the account;
no email, application, user ID, role or redirect is accepted in this body. Unknown fields are rejected.

Success: **200** Response<T>, payload null, message `Your password has been reset. Sign in with your new password.`
It issues no JWT or new session; route the user to that side's login. Refresh-cookie deletion affects
only that side's cookie. Sessions/refresh tokens of the reset account are invalidated in the database;
other independent accounts sharing the email are unaffected.

Failures: 400 invalid/missing fields or the generic `The password reset link is invalid or expired.`
for invalid, expired, wrong-side, used, disabled/deleted-account or stale-credential tokens;
429 excessive requests; 503 unconfigured/recovery persistence unavailable. API messages remain English.
No distinction between those invalid-token causes or account eligibility is exposed to the client.

## Security and delivery semantics

- 256-bit cryptographically random token; only SHA-256 token hash is used for lookup.
- Default expiry 15 minutes; configuration supports 5..30. Token is single-use, application-bound
  and bound to the current credential/email fingerprint. A direct password or email change invalidates prior links. The normalized recipient must exactly match the stored email; accent-insensitive database collation must never redirect a recovery email to a different mailbox.
- An eligible new link cancels unused older links for that same account/side. A 60-second account
  cooldown (configurable 60..900) prevents repeated link issuance; cooldown cases still return 202.
- The existing auth IP limiter separates recovery requests: forgot 5/minute and reset 10/minute,
  shared across the three sides. These counters are per Identity instance, not a distributed cluster quota.
- Reset transaction locks account -> token -> sessions, changes password, consumes all outstanding
  reset tokens for that account/side and revokes its sessions atomically. Concurrent token submissions
  succeed once. Session issuance rechecks current credentials under the same account lock: an old
  password verified just before reset cannot create a surviving session after reset.
- Link generation is based only on trusted server configuration, never Host headers or user redirect input.
  Existing Admin bootstrap/account creation and Tech enrollment/eligibility are not modified or bypassed.
- Pending delivery payloads are encrypted with ASP.NET Data Protection in Identity's durable outbox.
  The public request does not wait for SMTP. A worker leases one item at a time and calls Notification
  with a 30-second purpose/audience-bound service JWT tied to delivery ID/application/recipient hash.
  Ordinary App/Tech/Admin JWTs are rejected by that internal endpoint.
- Notification validates the configured reset-page URL and builds a fixed English email template.
  No arbitrary email subject/body/attachment API is exposed. Keep Notification private; **do not add a
  public Gateway route** for it. The existing Identity Gateway route already exposes the six public APIs.
- Transient failures retry with 30-second exponential backoff, capped at 15 minutes, until token expiry.
  Restarts preserve queued messages if the Data Protection keys are retained. Delivery is at-least-once:
  a lost acknowledgement may cause a duplicate email containing the same link, not an extra valid token.
  Sent/cancelled/expired pending payloads are cleared. Expired hashes/audit metadata are not automatically purged.
- SMTP TLS certificate verification is preserved. Plain SMTP is allowed only for a literal loopback
  IP in Development (used by the local integration test); production and nonloopback SMTP require TLS.
  This adapter supports SMTP relay/username-password authentication, not an OAuth-only provider adapter.
- Password recovery is disabled by default until deployment configuration is supplied. This avoids
  starting an outbox worker on an unmigrated Identity database and does not block existing login.

## Deployment

Publish **Identity.Api** and **Notification.Api**. Application/Domain/Infrastructure dependencies and
shared response assemblies are included with normal API publish. Existing Gateway routes are sufficient;
no Gateway republish, new Notification database or account reset is required for these APIs.

1. Back up Identity and manually apply `database/identity/010_password_recovery.sql` after the existing
   independent-account schema (008/009). **Never rerun destructive 008 on production.** The new migration
   creates only `identity_password_resets`, is additive and supports repeat execution of unchanged definitions.
2. Identity runtime account needs SELECT/INSERT/UPDATE on the new table, plus its existing permissions on
   the account/session tables. Use the real MySQL account host binding when granting privileges.
3. Configure a persistent private Data Protection key directory outside the web root. Grant only Identity's
   service identity read/write access, preserve it across deployments/restarts, and back it up with the database.
   Keys must not be published with source/artifacts or shared with the browser. An inaccessible/lost key ring
   prevents pending email decryption; do not delete it as a troubleshooting step.
4. Configure SMTP securely on Notification, the same Identity JWT signing key/issuer for the short-lived
   internal credential, and matching frontend reset-page URLs. Do not copy values into chat or frontend code.
5. Start Notification, configure/enable recovery on Identity, publish the three frontend reset pages and test
   receipt/reset/login for a controlled account in each side. Real provider credentials and delivery are not
   available in the cloud task; production SMTP/TLS/domain settings must be supplied on the deployment server.

Identity settings example (merge with existing server configuration; do not replace existing secrets):
```json
{
 "PasswordRecovery": {
   "Enabled":true,"LifetimeMinutes":15,"CooldownSeconds":60,
   "AppResetUrl":"https://app.zumaris.com/reset-password",
   "TechResetUrl":"https://tech.zumaris.com/reset-password",
   "AdminResetUrl":"https://admin.zumaris.com/reset-password"
 },
 "DataProtection":{"KeysPath":"C:\\ZumarisData\\IdentityKeys"},
 "Notification":{"BaseUrl":"http://127.0.0.1:5009/"}
}
```
Notification settings example (supply Username/Password and Jwt:Key securely, not in tracked files):
```json
{
 "Jwt":{"Issuer":"same-as-Identity"},
 "Smtp": {
   "Host":"smtp.your-provider.example","Port":587,"EnableTls":true,
   "FromAddress":"noreply@zumaris.com","FromName":"Zumaris"
 },
 "PasswordRecovery": {
   "AppResetUrl":"https://app.zumaris.com/reset-password",
   "TechResetUrl":"https://tech.zumaris.com/reset-password",
   "AdminResetUrl":"https://admin.zumaris.com/reset-password"
 }
}
```
Use `Smtp:Username` and `Smtp:Password` only if required by the chosen SMTP provider. Configure
Notification's Jwt:Key identically to Identity's key; the audience is the fixed internal
`Zumaris.Notification.PasswordRecovery`, not the frontend JWT audience. Notification has no Identity
session dependency for its purpose-bound service credentials. Bind it to the trusted private/loopback
listener and expose no public sender route. Default development Notification port is 5113;
set Identity Notification:BaseUrl accordingly. Remote service URLs must use HTTPS; loopback HTTP is allowed.

When multiple Identity instances are used, keep a consistent protected key ring/application name and
JWT settings across them. SQL leases prevent simultaneous normal dispatch, but preserve the at-least-once
handling noted above. Pending delivery tokens are time-limited and never returned through public diagnostics.

## Verification

`tests/Identity.Tests/PasswordRecoveryTests.cs` exercises separate application links, encrypted-vs-public
state, generic outcomes, token format, password confirmation and disabled configuration.
The guarded Identity MySQL suite checks real single-use concurrency, cross-side rejection, scoped
session revocation and stale-login credential rejection against a disposable loopback schema.
`scripts/Test-PasswordRecoveryApi.py` runs actual Identity/Notification processes, disposable MySQL,
a local SMTP receiver and a test-only Provider adapter: actual receipt for all three sides, persistence
and retry across restart/SMTP outage, old-password and old-refresh rejection, expiry/replay, CSRF,
rate limits, identical unknown-account response and rejection of frontend JWTs by Notification.
It does not establish real SMTP-provider delivery, production TLS trust/ACLs or live frontend wiring.
