-- Run manually on MySQL 8.0.16+. Non-destructive; application account needs SELECT/INSERT/UPDATE.
CREATE DATABASE IF NOT EXISTS zumaris_maintenance CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_maintenance;
CREATE TABLE IF NOT EXISTS equipment (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 category VARCHAR(32) NOT NULL,status VARCHAR(32) NOT NULL,details JSON NOT NULL,
 created_at DATETIME(6) NOT NULL,updated_at DATETIME(6) NULL,is_archived BOOLEAN NOT NULL DEFAULT 0,
 INDEX ix_equipment_property(property_id,is_archived,created_at,id),
 CONSTRAINT ck_equipment_category CHECK(category IN ('Heating','Electrical','Plumbing','Appliance','Safety','Other')),
 CONSTRAINT ck_equipment_status CHECK(status IN ('Active','NeedsService','OutOfService'))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS equipment_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 equipment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_type VARCHAR(40) NOT NULL,title VARCHAR(150) NOT NULL,occurred_at DATETIME(6) NOT NULL,
 INDEX ix_equipment_events(property_id,occurred_at,id),
 FOREIGN KEY(equipment_id) REFERENCES equipment(id)
) ENGINE=InnoDB;
-- Property IDs are external references; access is verified through Property API, never cross-schema SQL.
