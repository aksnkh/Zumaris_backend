# Technician verification documents

Implemented in Provider only. These are the final user-confirmed rules, superseding the earlier five-document design. Identity continues to own authentication and personal account details. No cross-service SQL or project references were added.

## Requirements

All technicians need independently approved `IdentityCheck`, `Insurance` and `Certificate`, a saved Postcode, an active Provider profile and the relevant self-declared category before receiving opportunities or submitting quotes. OtherSkills is optional free text; it grants no category permissions. Postcode is profile information, not an address-proof document or a separately reviewed item. It is normalized to uppercase, limited to 2–20 letters/digits/spaces/hyphens, without assuming a country or performing address verification.

IdentityCheck requires exactly one current identification file and an `identityKind`: `Passport`, `DrivingLicence` or `OtherId`. Do not send identityKind for Insurance or Certificate. No insurance coverage threshold, expiry requirement or extra qualification categories have been introduced.

## Technician APIs

Gateway prefix: `/provider`. A **Tech** Bearer token and an active Identity session are required for own-document routes; administrator routes require an **Admin** token plus server-side membership. App tokens cannot access these document APIs. See [application authentication](application-authentication.md) for login, enrolment and refresh changes. New JSON responses use `Response<T>`; data is under `payload`, English messages under `message`.

| Method | Provider-local path | Purpose |
|---|---|---|
| GET | `/api/providers/me/verification-documents` | Three document cards plus `profile.postcode` and `profile.otherSkills` |
| PUT | `/api/providers/me/verification-documents/profile` | Save `{ "postcode": "SW1A 1AA", "otherSkills": "Painting" }`; omit/null OtherSkills to clear |
| GET | `/api/providers/me/verification-documents/{documentType}` | Card, current upload, active verified file and all previous versions |
| POST | `/api/providers/me/verification-documents/{documentType}/files` | Multipart `file`; add `identityKind` for IdentityCheck only; creates Draft |
| POST | `/api/providers/me/verification-documents/{documentType}/submit` | `{ "documentId": "..." }` for the current file |
| POST | `/api/providers/me/verification-documents/submit` | Atomic `{ "documentIds": ["...", "..."] }`, 1–3 distinct current IDs |

Upload and submit are separate so View & upload can preview a draft before submission. PDF/JPEG/PNG signatures are checked, maximum 5 MiB each. The existing maximum of 20 stored files per provider, including immutable history, remains in force. Files remain private and use existing owner/admin attachment endpoints via the returned `downloadPath`; no public storage URL is added. Admin download paths require admin credentials.

## Administrator APIs

All paths below also use the `/provider` Gateway prefix. Administrator access is checked server-side against Identity; it is not granted by a request field or specialty declaration.

| Method | Provider-local path | Purpose |
|---|---|---|
| GET | `/api/admin/verification-documents?pageNumber=1&pageSize=20` | Only current submitted files, paginated up to 100 |
| GET | `/api/admin/providers/{providerId}/verification-documents` | Provider overview and profile information |
| GET | `/api/admin/providers/{providerId}/verification-documents/{documentType}` | Files and category review details |
| GET | `/api/admin/providers/{providerId}/verification-documents/history` | Audit events with administrator/actor and timestamp |
| POST | `/api/admin/providers/{providerId}/verification-documents/{documentType}/review` | `{ "documentId": "...", "approved": true, "feedback": null }` |

For rejection use `approved: false` with required English/user-provided feedback (maximum 1000 characters). Only an InReview current file can be decided. Stale files and competing opposite decisions return 409. Identical retries are idempotent. The previous whole-profile approval route now returns 409 directing clients to independent document review; a global approval cannot bypass the new requirements. Existing endpoint response shapes remain unchanged.

## Status and replacement

`NotUploaded → Draft → InReview → Verified / ActionRequired`. Rejected files must be replaced before resubmitting. Uploading a replacement changes `currentDocument` but preserves `verifiedDocument`; rejecting that replacement also preserves the previous approval. An approved replacement becomes the active verified file. An unsubmitted or superseded upload cannot be approved.

Each card has `isVerified` for the active approval and `status` for the current upload. Consequently an InReview/ActionRequired replacement can coexist with `isVerified: true`. Frontend header count = number of cards with isVerified, denominator = 3, percentage = rounded count × 100 / 3. Profile fields do not enter the count. The backend returns no count/percentage fields.

The existing protection against changing the Provider display identity is retained: changing displayName invalidates IdentityCheck only and requires a new upload/review; Insurance and Certificate remain approved. Saving Postcode/OtherSkills or uploading an unclassified file through the legacy API does not revoke independent approvals. Previous unclassified files are retained privately but are not automatically classified or approved.

## Database and rollout

Run `database/provider/004_independent_document_reviews.sql` manually against **zumaris_provider**, after 001/002/003, before deploying the new Provider build. The script creates four Provider-owned tables: per-file reviews, current/verified slots, audit history and verification profile. It does not delete old files or manufacture approvals. Existing technicians need their three documents individually reviewed and a Postcode saved under the new rules. Applying the final script twice is supported; CREATE IF NOT EXISTS does not migrate an unofficial earlier draft schema.

Publish/redeploy **Provider**. Identity, ServiceRequest, Billing and Gateway need no changes for this feature: ServiceRequest already obtains eligibility from Provider. No SQL was executed against the user's databases; integration checks use a disposable localhost MySQL schema.
