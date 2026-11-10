# Admin: technician profile and document review APIs

The requested capabilities already have endpoints in Identity, Provider and ServiceCatalog. No duplicate login or review endpoints were introduced. Provider's existing technician review queue is corrected to use **current submitted documents**, including replacement documents for technicians whose previous verification remains valid.

## Authentication

```http
POST https://api.zumaris.com/identity/api/auth/login
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "userName": "admin@example.com",
  "password": "your-password",
  "application": "Admin"
}
```

Use `payload.accessToken` as `Authorization: Bearer <token>` on every protected request. The Identity account must already have active membership in `platform_administrators`; selecting Admin does not grant that membership. The backend returns 403 when credentials are correct but membership is missing. Existing `/identity/api/auth/refresh?application=Admin` and `/identity/api/auth/logout?application=Admin` handle the application-specific HttpOnly refresh cookie, with `X-Zumaris-CSRF: 1` and browser credentials enabled. Administrator account creation is now available in Identity; see [administrator accounts](administrator-accounts.md). General role-management remains outside this scope. See [application authentication](application-authentication.md).

Provider validates both the Admin token context and current administrator access through Identity. App and Tech tokens cannot access these administration routes. Losing administrator membership blocks profile access, downloads, queue access and document decisions even if the old signed token still says Admin.

## Endpoint map

All Provider paths below use the base `https://api.zumaris.com/provider`. Placeholder `providerId`/`id` is a Provider profile ID, not an Identity user ID. Obtain it from a provider list or document queue item.

| Capability | Method | Provider-local path | Existing response |
|---|---|---|---|
| Technicians awaiting review | GET | `/api/admin/providers?pageNumber=1&pageSize=20` | `ProviderPage`, bare JSON |
| Profile and declared specialties | GET | `/api/admin/providers/{id}` | `ProviderAccount`, bare JSON |
| Current submitted document queue | GET | `/api/admin/verification-documents?pageNumber=1&pageSize=20` | `Response<PendingVerificationPage>` |
| All three document cards and profile postcode/skills | GET | `/api/admin/providers/{providerId}/verification-documents` | `Response<VerificationOverview>` |
| One document's current/verified file and previous versions | GET | `/api/admin/providers/{providerId}/verification-documents/{documentType}` | `Response<VerificationDocumentDetails>` |
| Private file download/view bytes | GET | `/api/admin/providers/{id}/documents/{documentId}` | Binary attachment |
| Approve or request changes | POST | `/api/admin/providers/{providerId}/verification-documents/{documentType}/review` | `Response<VerificationDocumentDetails>` |

These endpoints existed before this request. JSON response contracts are preserved: the old profile/list routes are not retrofitted into a new envelope. All document review endpoints already use `Response<T>`, and their data is in `payload`. All successful operations above return 200. Invalid page parameters return 400. Unknown profiles/documents return 404. Both page types have `items`, `pageNumber`, `pageSize` and `totalCount`; pageNumber starts at 1, pageSize is 1–100, default 20.

## Technician queue and specialties

`GET /api/admin/providers` now includes a technician if **at least one current file is InReview**. Multiple pending types produce one technician row. Draft uploads, rejected files waiting for replacement, completed decisions and superseded pending uploads do not themselves produce a queue row. A technician with all required active approvals still appears when a replacement is submitted for review. The count, page IDs, profiles and specialty IDs are read in the same database snapshot.

List/profile objects expose `providerId`, `userId`, `displayName`, `isActive`, `isVerified`, `onboardingStatus`, `revision`, `reviewReason`, `categoryIds` and `documents`. `isVerified` is eligibility from active required approvals and postcode; it is not a statement that no replacement needs review. Therefore an Approved/verified profile can legitimately appear in the pending review queue. The legacy revision is not an input to independent document review.

Specialties are `categoryIds`. Resolve their names/icons with the existing Admin-compatible lookup:

```http
GET https://api.zumaris.com/service-catalog/api/categories
Authorization: Bearer <ADMIN_ACCESS_TOKEN>
```

Each lookup item contains `id`, `name` and `iconUrl`. Specialties are self-declared and do not have a separate administrator approval action. Personal account data remains owned by Identity; this profile endpoint is the professional Provider profile.

## Document queue and file inspection

`GET /api/admin/verification-documents` gives one row per current InReview document. Each item has `providerId`, `providerName`, `documentType` and `document`. The latter contains `documentId`, `fileName`, `contentType`, upload/submission/review UTC timestamps, `status`, `reviewFeedback`, `downloadPath` and nullable `identityKind`.

`documentType` is `IdentityCheck`, `Insurance` or `Certificate`. IdentityCheck has one of `Passport`, `DrivingLicence` or `OtherId` as its identityKind. All three types are required for every technician.

For a technician overview, `payload.documents` contains the three cards; `payload.profile` contains postcode and optional otherSkills. Each card has `status`, `isVerified`, `feedback`, `currentDocument` and `verifiedDocument`. The detail endpoint additionally returns `payload.history`, the file versions for that document type.

Use the `downloadPath` returned by an **admin** response, relative to `https://api.zumaris.com`. It already contains `/provider`; do not add that prefix again. Fetch bytes with the Admin Bearer token. Existing downloads use `application/octet-stream`, attachment disposition and `nosniff`; a frontend PDF/image viewer can build a Blob using the trusted response metadata's PDF/JPEG/PNG contentType. The same protected endpoint serves current and historical files. A file ID under the wrong provider returns 404. File bytes are never wrapped in JSON or made publicly accessible.

## Independent decisions

```http
POST /provider/api/admin/providers/{providerId}/verification-documents/Insurance/review
Authorization: Bearer <ADMIN_ACCESS_TOKEN>
Content-Type: application/json
```

Approval:

```json
{
  "documentId": "22222222-2222-4222-8222-222222222222",
  "approved": true,
  "feedback": null
}
```

Request a correction:

```json
{
  "documentId": "22222222-2222-4222-8222-222222222222",
  "approved": false,
  "feedback": "Please upload a clear, complete copy."
}
```

Feedback is required for rejection and limited to 1000 characters. API-generated messages are English; administrator-entered content is preserved. Only the current submitted file can be decided. Approval yields Verified; rejection yields ActionRequired. Stale/changed uploads or conflicting decisions return 409. Identical repeated decisions are idempotent. Read document details again after a 409.

A rejected replacement does not revoke the previously approved file. The replacement's status can be ActionRequired while the card remains `isVerified: true`. Approving a replacement activates it instead of the old file. Reviewing one type never approves the other two. The old whole-profile `/verification` route returns 409 and must not be used by the admin frontend.

## Frontend sequence

1. Sign in as Admin.
2. Load either the paginated technician queue or the document queue.
3. On selection, fetch professional profile, specialty lookup and document overview.
4. Open the selected type's detail, inspect feedback and download the current file; inspect prior versions when needed.
5. Submit the exact current documentId with approve/reject and feedback.
6. Refresh the detail and queues. Once no current InReview files remain, the technician disappears from the review queue.

Header document totals/percentages remain frontend calculations from active `isVerified` values, denominator three. Postcode and OtherSkills are profile fields, not extra reviewable documents.

## Deployment and verification

This request changes only Provider's queue query, profile/list Swagger response metadata and tests. No new database script, Identity login route, role-grant endpoint or duplicated document route is needed. Existing Provider migrations 001–004 and Identity migration 006 remain prerequisites. Publish **Provider** to deploy the queue correction.

Regression coverage exercises draft exclusion, multiple-type deduplication, profile/category retrieval, replacement inclusion despite active verification, superseded-upload exclusion, pagination, old-file download and provider/file mismatch. HTTP checks also verify App/Tech denial and rejection of an Admin-context token without current server membership.
