# Frontend Passport API handoff

Scope: B1–B5 from the 14 November frontend handoff, existing Property/History/catalog APIs,
and the retained B6 Billing intent contract. No new promotion API is required for static cards.
The routes below are Gateway paths. New routes require deployment and the manual migrations below.

## Deployment and prerequisites

Publish Property.Api, Maintenance.Api, Document.Api, ServiceRequest.Api and YarpGateway.
Keep each service's JWT issuer/audience/key consistent with Identity; supply secrets through server
configuration, never frontend code. Set Identity:BaseUrl on all newly activated services and
Property:BaseUrl on Maintenance, Document and ServiceRequest. Property composition also requires
Maintenance:BaseUrl, Document:BaseUrl and ServiceRequest:BaseUrl. All URLs must be absolute HTTPS
(or loopback HTTP only), end with `/`, and contain no user info. Redirects are not followed.
Production defaults are loopback ports 5001/5002/5003/5004/5005 for Identity/ServiceRequest/Property/Document/Maintenance.
For development use 5105/5106/5107/5108/5109 respectively. Configure these URLs explicitly when those services run separately.
Approved CORS origins remain app.zumaris.com, tech.zumaris.com and admin.zumaris.com with credentials.

Configure Document `Documents:RootPath` to an absolute private directory outside the web root
(e.g. `C:\ZumarisData\PropertyDocuments` on IIS). Grant the Document process read/write access;
back up file storage with its metadata database. No public/static-file route exposes stored files.
IIS/Gateway limits must permit the 22 MiB multipart request envelope (file maximum 20 MiB).

Run manually, with schema administration rights:
- Property: `database/property/002_passport.sql` after existing 001.
- Maintenance: `database/maintenance/001_property_equipment.sql`.
- Document: `database/document/001_property_documents.sql`.
- ServiceRequest: existing migrations through 007, then `008_property_activity_index.sql`.
New Passport migrations/index are additive and repeatable; IF NOT EXISTS does not repair an incompatible schema.
Grant each actual application account SELECT/INSERT/UPDATE on its own schema only.
Do not reapply the one-time ServiceRequest 003 migration to existing databases.
No SQL runs on application startup. No user/production database was changed by implementation tests.

## Access and envelope

All new routes require a valid **App** JWT with an active Identity session. Reads require an active
Property membership and a nonarchived property. Writes require the property's original creator;
a declared Owner/Landlord role does not override that rule. Unknown/inaccessible properties and
records return 404; a member without write permission gets 403. Tech/Admin tokens do not grant
customer Passport access. Upstream services recheck the caller's token and property access on every call.

New JSON responses use the existing `Response<T>` with data in `payload`, English `message`,
`statusCode`, `title`, `messageLevel` (Cif), and the remaining existing envelope fields.
New creates return 201, reads/updates 200, archive returns bodyless 204. Validation returns 400
with an English top-level message and field-level `errors` for automatic model validation. New
handled errors use the response envelope. No global migration of existing endpoint response formats
was performed. File responses return actual bytes, not a JSON envelope.

Example new success:
```json
{"statusCode":200,"message":"Retrieved successfully.","title":"Success","messageLevel":"Cif","payload":{"items":[],"totalCount":0,"pageNumber":1,"pageSize":20}}
```
Existing Property endpoints return PropertyDto/PropertyPage directly. Their historical healthScore,
equipmentCount and documentCount remain null; use the new `/passport/metrics` for real counts.

Common pagination: pageNumber 1..100 (default 1), pageSize 1..100 (default 20). List endpoints
sort creation time descending, then ID descending. Timeline/event endpoints sort event time descending,
then event ID descending. Filters and type names are case-sensitive in the API contract.
Date-only values use YYYY-MM-DD. Timestamps use ISO 8601; send eventAt with an explicit offset (prefer Z).
Event timestamps returned by the unified timeline are UTC. Use the same from/to filters across pages;
these are live lists, not immutable snapshots. `from`/`to` are inclusive DateTimeOffset bounds.
Last-write-wins applies to metadata, rooms and manual events; there is no version field.
Archived items cannot be edited (409). Missing/degraded required dependencies fail closed (503),
never return fabricated zero metrics or a silently incomplete aggregate timeline.

## B1: Equipment — Maintenance

Prefix `/maintenance/api/properties/{propertyId}/equipment`:
- GET base: pageNumber/pageSize, optional category/status, archived=false.
- GET `/options`: categories Heating, Electrical, Plumbing, Appliance, Safety, Other;
  statuses Active, NeedsService, OutOfService. Archive is independent of operational status.
- GET `/{id}`: Equipment including archived flag, details, createdAtUtc/updatedAtUtc.
- POST base: EquipmentInput, returns the persisted Equipment.
- PUT `/{id}`: complete EquipmentInput replacement.
- DELETE `/{id}`: soft archive, repeatable; no file/history deletion.
- GET `/events`: paginated equipment-created/updated/archived events, optional from/to/type.

EquipmentInput example:
```json
{"name":"Boiler","category":"Heating","status":"Active","brand":"Example","model":"Model A","serialNumber":"ABC123","installedOn":"2020-01-01","warrantyStartsOn":"2020-01-01","warrantyExpiresOn":"2030-01-01","location":"Kitchen","lastServicedOn":"2025-12-01","nextServiceOn":"2026-12-01","serviceRequestIds":[]}
```
Name (1..150), category and status required. Optional brand <=100, model/serial <=150,
location <=200. Warranty/service date pairs must be chronological. Up to 20 distinct nonempty
serviceRequestIds are allowed; each must be readable by the caller and belong to the same property.
Equipment response has equipmentId, propertyId, nested `details`, timestamps, archived,
relatedDocumentsUrl and serviceRequestUrls. Documents are linked via their own equipmentId;
there is no cross-service SQL or speculative service-history record.

## B2: Property documents — Document

Prefix `/document/api/properties/{propertyId}/documents`:
- GET base: pageNumber/pageSize, optional type/equipmentId/requestId, archived=false.
- GET `/options`: allowed document types, content types, maxFileBytes and archive retention.
- GET `/{id}`: metadata and protected downloadUrl.
- POST base: multipart/form-data with file, title, type; optional expiresOn/equipmentId/requestId.
- PUT `/{id}`: JSON metadata replacement (title/type/optional links and expiry); file is immutable.
- DELETE `/{id}`: soft archive; authorized file access and audit history remain available.
- GET `/{id}/file`: actual authenticated file bytes as attachment; `preview=true` uses inline
  delivery with sandbox CSP and nosniff. No public or credential-bearing download URL.
- GET `/events`: paginated upload/update/archive events, optional from/to/type.

Types: Certificate, Invoice, Report, Warranty, Manual, Insurance, FloorPlan, Other.
Only signature-checked PDF/JPEG/PNG files, 1 byte..20 MiB. File extension/client MIME is not trusted.
Upload envelope limit 22 MiB; title 1..200, filename <=200. Equipment and request references
are verified through the owning API and must belong to the selected property; unknown references
return 404, cross-property request references 400, archived equipment 409.
Response: documentId, propertyId, nested details (title/type/expiresOn/equipmentId/requestId),
fileName, contentType, sizeBytes, sha256, uploadedAtUtc, updatedAtUtc, archived, downloadUrl.
Unavailable storage returns 503. Files are retained on an uncertain metadata/commit outcome; reconcile unreferenced storage files after an outage rather than deleting potentially committed file bytes. No physical-delete endpoint or automatic malware scan is claimed.

## B3: Manual events and unified timeline — Property

Prefix `/property/api/properties/{propertyId}`:
- GET `/events`: manual events, pageNumber/pageSize, optional from/to.
- GET `/events/{id}`: manual event detail.
- POST `/events`: manual event, returns 201.
- PUT `/events/{id}`: replace manual event data.
- DELETE `/events/{id}`: archive event; removed from active timeline.
- GET `/timeline`: combined manual/equipment/document/service-status events. Optional source
  Manual|Equipment|Document|ServiceRequest, type, from/to and common pagination.

Example manual event:
```json
{"eventAt":"2026-11-14T10:00:00Z","title":"Roof inspection","description":"Annual inspection completed","cost":75.00,"currencyCode":"GBP"}
```
Title 1..100, description <=4000. Cost/currency must both be supplied or both null;
cost 0..9999999999.99, up to two decimals; currency three uppercase letters. Supported manual type
is ManualEvent. Arbitrary equipment/document links are not accepted on manual notes; real source
records link automatically through the unified feed.

Unified event: eventId (source-prefixed stable ID), propertyId, occurredAtUtc, source, type,
title, description, relatedId, optional cost/currencyCode. Types: ManualEvent;
EquipmentCreated/EquipmentUpdated/EquipmentArchived; DocumentUploaded/DocumentUpdated/DocumentArchived;
RequestPending/RequestAssigned/RequestInProgress/RequestCompleted/RequestCancelled.
Source-specific dates retain their actual meaning: manual event date vs source mutation/status time.
Service events remain caller-owned requests only; property membership does not expose another
customer's private requests, reports or photos.

## B4: Real metrics and retained service APIs

GET `/property/api/properties/{propertyId}/passport/metrics`:
```json
{"propertyId":"guid","healthScore":null,"healthScoreStatus":"NotConfigured","equipmentCount":1,"documentCount":2,"roomCount":3,"floorAreaSquareMetres":85.50,"serviceSummary":{"total":4,"open":2,"upcoming":1,"completed":1,"cancelled":1}}
```
This object is inside the new response payload. Counts exclude archived records; zero is returned
only when confirmed from the relevant real database. HealthScore stays null because no approved
scoring formula/data completeness policy exists. No synthetic health date/score is generated.
ServiceSummary includes only the current customer's requests for the selected property:
Open=Pending+Assigned+InProgress. Upcoming=Pending/Assigned with preferredDate >= today's UTC date.
Upcoming is a subset of Open, not an additional status. Emergency requests without a preferred date
are not Upcoming. Dates represent preferences, not guaranteed appointments.

GET `/service-request/api/properties/{propertyId}/service-summary`: the same service-summary counts.
GET `/service-request/api/properties/{propertyId}/service-events`: paginated own request status events,
optional from/to/type. These new endpoints also enforce Property membership.
Retain existing GET `/service-request/api/service-history` (All/Completed/Cancelled/Paid filters)
and GET `/service-request/api/service-history/{id}`, plus GET `/service-request/api/service-requests`
and GET `/service-request/api/service-requests/{id}/timeline`. Existing request lists use page/pageSize
and status Pending|Assigned|InProgress|Completed|Cancelled; they keep their existing response shapes.

## B5: Actual rooms and floor area — Property

Prefix `/property/api/properties/{propertyId}`:
- GET/PUT `/passport/profile`: `{ "floorAreaSquareMetres": 85.50 }` (nullable).
- GET `/rooms/options`: supported room types and m2 area unit.
- GET `/rooms`: common pagination, archived=false.
- GET `/rooms/{id}`.
- POST `/rooms`, PUT `/rooms/{id}`: `{ "name":"Kitchen", "type":"Kitchen", "floor":"Ground", "floorAreaSquareMetres":12.50 }`.
- DELETE `/rooms/{id}`: soft archive.

Room types: Bedroom, Bathroom, Kitchen, LivingRoom, DiningRoom, Hallway, Utility, Garage, Garden, Other.
Name 1..100, floor <=50; area nullable or >0..1000000 m2 with up to two decimal places.
Room response contains roomId/propertyId/details/createdAtUtc/updatedAtUtc/archived.
Room inventory is independent of existing bedrooms/bathrooms totals; no automatic inferred totals
or estimated floor area is stored. Generic `/service-catalog/api/areas` is not a property room inventory.

## Existing APIs retained for this frontend part

Property: GET/POST `/property/api/properties`, GET/PUT/DELETE `/property/api/properties/{id}`,
GET `/property/api/properties/current`, PUT `/property/api/properties/{id}/current`,
GET `/property/api/property-options`. Profile address and property-count badge use these existing APIs.
For category-based specialty cards, reuse GET `/service-catalog/api/categories` and
GET `/service-catalog/api/categories/{id}` when semantically appropriate. No promotion persistence API was introduced.

B6 Billing: retain POST `/billing/api/payments` with `{ "requestId":"guid" }` and
GET `/billing/api/payments/{id}`. POST returns the existing message/payload shape; GET returns
PaymentIntent directly. Only own Completed work with the accepted positive fixed-price GBP quote qualifies.
Current gateway=PayPal, status=AwaitingGateway, canPay=false, checkoutUrl=null. These are not checkout
success or stored-payment-method APIs. Saved methods, real checkout, webhooks and settlement remain
unavailable: the handoff requests a provider strategy decision, the prior approved scope deferred
these flows, and no PayPal configuration/credentials are present. Credentials must be supplied securely,
never in the frontend or chat. No untested payment routes or fabricated success states are listed.

## Validation evidence

`dotnet build Zumaris.sln` and the existing .NET suites pass (database tests without configured
per-suite databases are explicitly skipped). `scripts/Test-PassportApi.py` starts actual Property,
Maintenance, Document, ServiceRequest and Gateway processes with a disposable MySQL 8 database
and a test-only Identity session stub. It checks CRUD, persistence across process restart, exact file
bytes, property/role/creator isolation, metadata validation, source aggregation, real zero counts,
revoked sessions, dependency outages and gateway routes. It does not test a real PayPal service,
production Identity deployment, production database migrations or health scoring.
