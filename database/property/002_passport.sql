-- Manual, additive MySQL 8.0.16+ migration after 001_properties.sql. No existing property data is deleted.
USE zumaris_property;
CREATE TABLE IF NOT EXISTS property_passport_profiles (
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 floor_area_sqm DECIMAL(12,2) NULL,
 FOREIGN KEY(property_id) REFERENCES properties(id),
 CONSTRAINT ck_passport_area CHECK(floor_area_sqm IS NULL OR floor_area_sqm>0 AND floor_area_sqm<=1000000)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS property_rooms (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,details JSON NOT NULL,
 created_at DATETIME(6) NOT NULL,updated_at DATETIME(6) NULL,is_archived BOOLEAN NOT NULL DEFAULT 0,
 INDEX ix_rooms_property(property_id,is_archived,created_at,id),FOREIGN KEY(property_id) REFERENCES properties(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS property_manual_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,details JSON NOT NULL,event_at DATETIME(6) NOT NULL,
 created_at DATETIME(6) NOT NULL,updated_at DATETIME(6) NULL,is_archived BOOLEAN NOT NULL DEFAULT 0,
 INDEX ix_manual_events(property_id,is_archived,event_at,id),FOREIGN KEY(property_id) REFERENCES properties(id)
) ENGINE=InnoDB;
-- Existing Property application account needs SELECT/INSERT/UPDATE on these tables. No new identity grants or cross-schema queries.
