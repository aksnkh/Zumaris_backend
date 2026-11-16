# Health Score — self-reported method 1.0

PropertyIntelligence owns calculation, assessment storage and calculation history. Maintenance
owns actual equipment/status/service dates, Document owns actual files/expiry metadata, Property
owns membership and creator authorization. Only their APIs are used; no cross-schema SQL or
cross-service project references are introduced.

This version follows the approved 35/30/25/10 proposal and the user's confirmed **self-reported**
assessment option. It is not an administrator review, professional inspection, legal compliance
certificate or proof of safety. Data coverage describes completeness of supplied information,
not its truthfulness or professional reliability. All responses label `assessmentSource: SelfReported`.

## Calculation

Each component is 0..100; an unknown component is null. The final score is produced only when
all components are known. Never redistribute a missing component's weight to other components.
Compute with decimal arithmetic, round the final result to the nearest integer (halves away from zero).
A high average does not suppress `criticalAlerts`: frontends must always display those independently.

| Component | Weight | Method 1.0 rules |
|---|---:|---|
| Safety | 0.35 | Mean of applicable checks. Satisfactory self-review + linked active, appropriate, unexpired document = 100. Expired document or unsatisfactory review = 0. Missing evidence/review/expiry information = unknown. |
| Equipment | 0.30 | Importance-weighted equipment status: Active=100, NeedsService=50, OutOfService=0. Unknown status remains unknown. |
| Maintenance | 0.25 | Same importance weights. Service deadline today/future=100; overdue 1–30 days=75, 31–90=40, >90=0. Missing deadline is unknown unless explicitly exempt from servicing. |
| Issues | 0.10 | 100 minus penalties of unresolved declared issues, minimum 0. Low=10, Medium=20, High=30, Critical=60; add 5 per full 30 days open, capped at 30 extra per issue. Resolved issues have no penalty. |

`HealthScore = Safety × 0.35 + Equipment × 0.30 + Maintenance × 0.25 + Issues × 0.10`

The proposal's example remains `80×.35 + 70×.30 + 60×.25 + 90×.10 = 73`.
The four safety checks are ElectricalSafety, GasSafety, EnergyPerformance and Insurance. These are
an applicability questionnaire, not a claim that all four are legally mandatory for every property.
Each must be marked Required or NotApplicable; NotApplicable requires an explanation of 10..500
characters and no document/review/expiry declaration. All four must be supplied to confirm the safety inventory.
Applicable checks require a live linked document in the same property: Insurance uses document type
Insurance; the other checks use Certificate or Report. Review choices are Unknown, Satisfactory,
Unsatisfactory. Upload alone never earns positive points. If the document has no expiry date,
`hasNoExpiry=true` must be explicitly declared; it cannot override an actual recorded expiry date.
Expiry on today's UTC date remains valid; the next UTC day is expired. Changing linked document
metadata invalidates the previous assessment review until the creator saves a new assessment.

Equipment importance: Heating/Electrical/Safety=3; Plumbing=2; Appliance/Other=1. Only nonarchived
records are used. The creator must explicitly confirm equipment inventory completeness and at least
one current equipment record must exist. This version does not calculate a total for an empty inventory.
A future lastServicedOn or last service after its next deadline makes maintenance data unknown.
Servicing exemptions only apply when no nextServiceOn is recorded; they never hide an overdue deadline.
Unknown/archived/stale exemption IDs do not add any points.

Known issues must be explicitly declared complete, including an explicitly empty inventory when
none are known. No issues table/requests being empty is treated as evidence of health. The questionnaire
is the source of known-issue data; creating orders, accepting quotes, payments or reviews earns no points.
Open critical issues, unsatisfactory/expired gas or electrical checks and out-of-service Heating,
Electrical or Safety equipment create independent critical alerts, even if the final score is null.

Coverage is the weighted sum of known-data percentages. Safety coverage is the fraction of applicable
checks with usable data after the safety inventory is confirmed (all explicitly NotApplicable = 100%).
Equipment and Maintenance coverage use known equipment importance / total importance after inventory
confirmation. Issues coverage is 100 only after explicit inventory confirmation. Before required
confirmations, relevant coverage is zero. If coverage is insufficient, `status=InsufficientData` and
`healthScore=null`; `components` and `improvementActions` explain what is missing. A calculated score
has `status=Calculated`. UTC time and `methodVersion=1.0` are included in every result.

## Frontend APIs (Gateway paths)

All endpoints require an App JWT and active Identity session. Readers must be current active Property
members. Only the property creator can PUT assessments or persist calculation snapshots. An Owner,
Tenant or Admin label alone does not confer creator access. Tech/Admin tokens cannot use these routes.
Unknown/inaccessible properties return 404; members without write permission get 403.
All new JSON responses use the existing Response<T> envelope, with data in `payload`.

| Method | Path | Purpose |
|---|---|---|
| GET | `/property-intelligence/api/properties/{propertyId}/health-score/options` | Version, coefficients and allowed questionnaire values |
| GET | `/property-intelligence/api/properties/{propertyId}/health-score/assessment` | Saved declaration; payload null if none exists |
| PUT | `/property-intelligence/api/properties/{propertyId}/health-score/assessment` | Replace and audit the declaration; last-write-wins, no input score allowed |
| GET | `/property-intelligence/api/properties/{propertyId}/health-score` | Calculate from current profile, current date and live owning-service evidence; no snapshot write |
| POST | `/property-intelligence/api/properties/{propertyId}/health-score/calculate` | Calculate and persist an audit snapshot (including insufficient-data results) |
| GET | `/property/api/properties/{propertyId}/passport/metrics` | Existing real counts plus live healthScore, healthScoreStatus and healthScoreDetails |

PUT body example (replace GUIDs with actual active records belonging to the property):
```json
{
  "safetyInventoryComplete": true,
  "equipmentInventoryComplete": true,
  "issueInventoryComplete": true,
  "safetyChecks": [
    {"code":"ElectricalSafety","applicability":"Required","review":"Satisfactory","documentId":"11111111-1111-1111-1111-111111111111"},
    {"code":"GasSafety","applicability":"NotApplicable","notApplicableReason":"The property has no gas installation."},
    {"code":"EnergyPerformance","applicability":"Required","review":"Satisfactory","documentId":"22222222-2222-2222-2222-222222222222","hasNoExpiry":true},
    {"code":"Insurance","applicability":"Required","review":"Satisfactory","documentId":"33333333-3333-3333-3333-333333333333"}
  ],
  "serviceNotRequiredEquipmentIds": [],
  "issues": [
    {"issueId":"44444444-4444-4444-4444-444444444444","title":"Leaking tap","severity":"Low","openedOn":"2026-11-15","resolvedOn":null}
  ]
}
```
Partial declarations are allowed when corresponding completeness flags are false. At most four distinct
safety checks, 500 distinct servicing-exemption IDs and 200 distinct issues; request size <=256 KiB.
Issue title 1..300, severity Low/Medium/High/Critical. Dates are YYYY-MM-DD, chronological, >=1900,
and not in the future. Saving a profile verifies referenced documents/equipment through their owning
APIs and rejects missing/archived/cross-property IDs (400). Existing same-property documents may need
metadata correction before they satisfy safety rules. Direct score submissions/unknown body fields are rejected.

Payload example:
```json
{
 "propertyId":"guid", "healthScore":73, "status":"Calculated", "dataCoveragePercent":100,
 "calculatedAtUtc":"2026-11-15T12:00:00Z", "methodVersion":"1.0", "assessmentSource":"SelfReported",
 "assessmentUpdatedAtUtc":"2026-11-15T11:00:00Z",
 "components":[{"code":"Safety","weight":0.35,"score":80,"dataCoveragePercent":100,"weightedContribution":28}],
 "criticalAlerts":[], "improvementActions":[]
}
```
The example omits three components for brevity; actual responses always contain all four. Actions
have code/message/optional relatedId. Messages are English; user-provided issue titles remain unchanged.
Counters alone never stand in for scoring evidence. Every source page is fetched (up to 10,000 records
per source); incomplete/invalid source responses or outages return 503, not a fabricated healthy score.
Multi-service reads are a live view rather than a distributed immutable snapshot; retry if evidence
changes during pagination. Property metrics keep existing counts available if PropertyIntelligence
is temporarily unavailable: healthScore=null, healthScoreStatus=Unavailable, healthScoreDetails=null.
Existing PropertyDto/list/current health-score placeholders remain unchanged; clients should use metrics
or the dedicated score endpoint for this calculation.

## Deployment and storage

Publish **PropertyIntelligence.Api**, **Property.Api** and **YarpGateway**. No Maintenance or Document
schema/code changes are needed. Manually run `database/property-intelligence/001_health_scores.sql`
with a DBA account; it creates only the new service-owned schema/tables, supports repeated execution
of unchanged definitions, and never runs at startup. App account needs SELECT/INSERT/UPDATE on
`zumaris_property_intelligence.*`; grant separately for the real account host. No user database was
modified during implementation.

Configure PropertyIntelligence: ConnectionStrings:DefaultConnection, Identity JWT key/issuer/audience,
Identity:BaseUrl, Property:BaseUrl, Maintenance:BaseUrl and Document:BaseUrl. Configure Property
PropertyIntelligence:BaseUrl. Use HTTPS or trusted loopback HTTP, with a trailing slash and no credentials
in URLs. Production defaults: PropertyIntelligence 5010, Identity 5001, Property 5003, Maintenance 5005,
Document 5004. Development: 5114/5105/5107/5109/5108 respectively. Maintain approved three-origin CORS.
Secrets remain on the server, never in frontend code, saved SQL or chat.

Current declarations and their audit history are persisted. Explicit POST calculations append result
snapshots with actor, UTC calculation time and method version. GET always recomputes using current
evidence/date, so previous snapshots are not silently reused after expiry or a source update. Input
mutations remain last-write-wins and are auditable; a real professional review workflow is not introduced.
