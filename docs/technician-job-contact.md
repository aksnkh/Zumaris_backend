# Technician job destination and customer contact

## Public API changes

The existing routes retain their envelopes/pagination/filter behavior:

- `GET /service-request/api/technician/jobs`
- `GET /service-request/api/technician/jobs/{id}`

Each job now includes four nullable fields alongside its existing `propertyAddress`:
```json
{
  "propertyAddress":"12 Example Road, London, SW1A 1AA",
  "latitude":51.556,
  "longitude":-0.192,
  "customerName":"Alex Customer",
  "customerPhone":"+447700900123"
}
```
List rows expose these fields directly inside `items[]`; detail returns the same job shape.
The shared job DTO also carries these additive fields in existing start/complete responses and
customer-owned execution details. No new client input is required. Accepted-quote input/output
contracts remain unchanged. The fields are **not** added to opportunities, quote-context or quotes.
Missing values remain null; email/username are not substituted for a missing customer name.

## Capture and authorization

When the owning **App customer** accepts a Pending quote at
`POST /service-request/api/quotes/{id}/accept`, ServiceRequest reads:

- `GET /identity/api/profile/me` with the accepting customer's own Bearer token, for first name,
  last name and phone. The returned user ID must equal the request owner. This is the request's
  customer, not the property creator, current resident or the selected provider.
- `GET /property/api/properties/{request.PropertyId}` with the same token, for the actual request
  property's address/latitude/longitude. It never reads the customer's current-property selection.
  The returned property ID must match; normal Property membership authorization still applies.

Both reads occur before assignment. Their result is saved inside the same ServiceRequest transaction
that accepts the quote, assigns its provider, increases the request version and withdraws competing
Pending quotes. No cross-service SQL, project references, privileged contact endpoint or end-user
impersonation token is used. Declared source values may be null; a source 404 is treated as unavailable
record data and leaves those values null (an existing request address is retained when no new address
is available). Auth/source outages, redirects, malformed/mismatched records or invalid coordinates
return 503 before assignment; they are not silently frozen as an empty snapshot. Retry acceptance
when the source is restored.

After acceptance the job uses only its stored snapshot; it makes no profile/property lookup when
reading Jobs. Later changes to the customer's phone/name or property address/coordinates do not
change the accepted job. Repeating Accept on the already accepted quote does not recapture/overwrite
its snapshot and does not require the contact sources to be available. Existing Identity session
validation still applies to protected requests; snapshotting does not remove that authentication dependency.

Only the selected technician can read the job/contact through Tech Jobs, using the existing
accepted-quote/provider-user relationship. Another technician receives 404. An App token cannot
use Tech Jobs; the owning customer retains its existing execution-detail access. No contact/location
is published to technicians before quote acceptance. Cancelled/completed jobs retain their existing
selected-provider history access. Coordinate range: latitude -90..90 and longitude -180..180;
either both are present or both are null. Source URLs must be HTTPS or trusted loopback HTTP,
with a trailing slash and no embedded credentials; redirects are disabled.

## Database and publish

Manually apply `database/service-request/009_job_contact_snapshot.sql` after migrations 001..008,
then publish **ServiceRequest.Api**. Identity, Property and Gateway need no code change or republish
for this feature. The existing public source APIs must already be available and use compatible JWT
settings. Verify ServiceRequest Identity:BaseUrl and Property:BaseUrl (production defaults
`http://127.0.0.1:5001/` and `http://127.0.0.1:5003/`; development 5105/5107).

The migration adds job_latitude, job_longitude, job_customer_name, job_customer_phone and
job_contact_captured_at, plus a nullable-pair/range check. It is additive and repeatable, changes no
existing data, and never runs at startup. Apply it before running the new application so reads do
not encounter missing columns. Existing schema-level SELECT/INSERT/UPDATE permissions suffice;
no Identity/Property database grants are required for ServiceRequest.

The user confirmed that legacy orders do not matter before launch: **there is no legacy backfill**.
Existing rows may keep null contact fields. No bulk reset, source-data migration or destructive SQL
is included. New orders accepted with this implementation receive the capture-time snapshot.

## Validation

- Unit tests exercise customer-token forwarding, the exact request-property endpoint, missing fields,
  protection against wrong customer/property IDs, and source failures.
- The guarded MySQL workflow exercises atomic snapshot persistence, unavailable-source rollback,
  identical list/detail contact fields, unauthorized technician rejection and idempotent acceptance.
- `scripts/Test-PassportApi.py` uses real Property/ServiceRequest processes and disposable MySQL,
  with test-only Identity/Provider/catalog adapters. It verifies the complete acceptance-to-job flow,
  unchanged snapshots after profile/property changes, successful job reads while contact sources are
  unavailable, no preaccept exposure and repeatable migration without changing captured data.
- Tests do not apply SQL to production or establish production source URLs, grants or frontend map/phone wiring.
