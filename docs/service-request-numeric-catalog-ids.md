# Numeric Category and Area IDs in ServiceRequest

Creation and editing now accept `categoryId` and `areaTypeId` as positive integers. Request details, lists and history detail return numeric category IDs as well. `propertyId`, request IDs, users and technicians remain GUIDs. Stored SQL parameters use integers and readers use GetInt32. No cross-service SQL joins or foreign keys are introduced.

The IDs are obtained from ServiceCatalog's Area and Category lists. This change validates positive integer shape only; checking actual catalog existence via HTTP remains separate work. No catalog lookup, ownership lookup or invented GUID-to-integer conversion is implied.

Example of a request without a schedule (only Emergency may omit the schedule):

```json
{
  "propertyId": "<actual property GUID>",
  "categoryId": 1,
  "areaTypeId": 1,
  "description": "The kitchen tap is leaking and needs repair.",
  "urgency": "Emergency",
  "preferredDate": null,
  "preferredStartTime": null,
  "preferredEndTime": null
}
```

For Standard/Priority supply a future date plus start/end as local whole-second times, for example `10:00:00` and `12:00:00` in the configured scheduling time zone. End must be later than start. `Idempotency-Key`, if provided, must be 8–128 allowed characters; do not reuse a key for a different order.

## Upgrade an existing database

1. Back up `zumaris_service_request` and stop ServiceRequest while migrating. The migration involves DDL and is not transactionally reversible as a whole. Do not run it concurrently with order writes.
2. Run `database/service-request/004_numeric_catalog_ids.sql` manually as root/admin on MySQL 8.0.16+. It creates local mapping tables. If legacy GUID-based orders exist without mappings, it raises a clear error BEFORE modifying their request columns/data.
3. Inspect old values and populate reviewed mappings using the real Category and Area IDs from the catalog. Mapping tables are local migration aids, not duplicated catalogs. Never guess which GUID means which category/area.

```sql
USE zumaris_service_request;
SELECT DISTINCT category_id FROM service_requests;
SELECT DISTINCT area_type_id FROM service_requests;
-- Replace placeholders with reviewed real values:
-- INSERT INTO category_catalog_id_map (legacy_id, catalog_id) VALUES ('<old GUID>', 1);
-- INSERT INTO area_catalog_id_map (legacy_id, catalog_id) VALUES ('<old GUID>', 1);
```

4. Rerun 004 after all mappings are supplied. An empty legacy table needs no mappings. The cutover sets the original column names to INT and preserves old values in nullable `category_id_legacy_guid` and `area_type_id_legacy_guid`. Existing requests, histories and photos are not deleted. Reruns after success detect the INT columns and leave them alone. Keep mapping/legacy columns for audit; no automatic cleanup is performed.
5. Publish ServiceRequest.Api and update all clients to send integer catalog IDs, then restart its pool. Do not run the previous binary against the converted schema. Identity, ServiceCatalog and Gateway do not need changes for this contract update.
6. Validate create, read, edit and history on the server. Never use new test tokens for ServiceRequest; its existing JWT/session requirements are unchanged.

Existing idempotency hashes are preserved. A pre-migration GUID payload and a new numeric payload are different bodies; replaying an old key with converted IDs returns 409 instead of creating a duplicate. Reconcile the original order rather than blindly submitting it under another key. For genuinely new orders use new keys.

Restore the backup and matching old binary together if rollback is required; changing only the binary is not a rollback.

## New databases

Run updated 001, then 002 and 003 in order. New 001 creates integer columns with positive-ID checks. 004 is optional on such a fresh schema and safely detects the already-converted columns. Do not expect rerunning 001 to convert an existing table.

## Validation

The ServiceRequest suite covers numeric request inputs, rejection of nonpositive IDs/legacy GUIDs, request lifecycle/history and idempotency. The manual SQL was also exercised against isolated MySQL 8 for an empty legacy schema, blocked unmapped data, reviewed mapping cutover, preservation of GUIDs/hashes, insertion after conversion, fresh schema and reruns. The user's database and IIS were not accessed.
