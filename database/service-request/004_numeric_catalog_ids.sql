-- MANUAL migration for MySQL 8.0.16+. Stop ServiceRequest and take a backup first.
-- Existing GUIDs have no intrinsic relationship to numeric catalog IDs.
-- On nonempty legacy tables populate BOTH mapping tables with reviewed values, then rerun.
-- No cross-schema reads, invented mappings, dropped requests or automatic runtime SQL.
USE zumaris_service_request;
CREATE TABLE IF NOT EXISTS category_catalog_id_map (
    legacy_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    catalog_id INT NOT NULL,
    CONSTRAINT ck_category_catalog_mapping CHECK (catalog_id > 0)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS area_catalog_id_map (
    legacy_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    catalog_id INT NOT NULL,
    CONSTRAINT ck_area_catalog_mapping CHECK (catalog_id > 0)
) ENGINE=InnoDB;
-- Inspect legacy values before inserting reviewed mappings:
-- SELECT DISTINCT category_id FROM service_requests;
-- SELECT DISTINCT area_type_id FROM service_requests;
-- INSERT INTO category_catalog_id_map VALUES ('<real old GUID>', <actual category ID>);
-- INSERT INTO area_catalog_id_map VALUES ('<real old GUID>', <actual area ID>);

DROP PROCEDURE IF EXISTS migrate_service_request_catalog_ids;
DELIMITER $$
CREATE PROCEDURE migrate_service_request_catalog_ids()
main: BEGIN
    DECLARE category_type VARCHAR(64);
    DECLARE area_type VARCHAR(64);
    SELECT data_type INTO category_type FROM information_schema.columns
        WHERE table_schema=DATABASE() AND table_name='service_requests' AND column_name='category_id';
    SELECT data_type INTO area_type FROM information_schema.columns
        WHERE table_schema=DATABASE() AND table_name='service_requests' AND column_name='area_type_id';
    IF category_type='int' AND area_type='int' THEN
        SELECT 'Already uses INT catalog IDs; no changes applied.' AS result;
        LEAVE main;
    END IF;
    IF category_type IS NULL OR area_type IS NULL OR category_type <> 'char' OR area_type <> 'char' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Unexpected schema: expected both legacy CHAR columns or both INT columns.';
    END IF;
    IF EXISTS (SELECT 1 FROM service_requests r
        LEFT JOIN category_catalog_id_map c ON c.legacy_id=r.category_id
        LEFT JOIN area_catalog_id_map a ON a.legacy_id=r.area_type_id
        WHERE c.catalog_id IS NULL OR a.catalog_id IS NULL) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Missing reviewed catalog mappings. Populate category_catalog_id_map and area_catalog_id_map, then rerun.';
    END IF;
    -- Nullable legacy columns permit new requests after cutover; old values remain for audit.
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE()
        AND table_name='service_requests' AND column_name='category_id_numeric') THEN
        ALTER TABLE service_requests ADD COLUMN category_id_numeric INT NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE()
        AND table_name='service_requests' AND column_name='area_type_id_numeric') THEN
        ALTER TABLE service_requests ADD COLUMN area_type_id_numeric INT NULL;
    END IF;
    UPDATE service_requests r
        JOIN category_catalog_id_map c ON c.legacy_id=r.category_id
        JOIN area_catalog_id_map a ON a.legacy_id=r.area_type_id
        SET r.category_id_numeric=c.catalog_id, r.area_type_id_numeric=a.catalog_id;
    IF EXISTS (SELECT 1 FROM service_requests WHERE category_id_numeric IS NULL OR area_type_id_numeric IS NULL) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Mapping incomplete; no column cutover performed. Keep ServiceRequest stopped.';
    END IF;
    ALTER TABLE service_requests
        CHANGE COLUMN category_id category_id_legacy_guid CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
        CHANGE COLUMN area_type_id area_type_id_legacy_guid CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
        CHANGE COLUMN category_id_numeric category_id INT NOT NULL,
        CHANGE COLUMN area_type_id_numeric area_type_id INT NOT NULL,
        ADD CONSTRAINT ck_service_requests_category CHECK (category_id > 0),
        ADD CONSTRAINT ck_service_requests_area CHECK (area_type_id > 0);
    SELECT 'Catalog IDs migrated. Legacy GUIDs and old idempotency hashes retained.' AS result;
END$$
DELIMITER ;
CALL migrate_service_request_catalog_ids();
DROP PROCEDURE IF EXISTS migrate_service_request_catalog_ids;
SELECT id, category_id, area_type_id FROM service_requests ORDER BY created_at;
