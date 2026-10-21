-- Manual script for MySQL 8.0.16+. No Identity/ServiceRequest tables or user data are changed.
-- IF NOT EXISTS allows an unchanged rerun; it does not repair incompatible existing definitions.
CREATE DATABASE IF NOT EXISTS zumaris_property CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_property;
CREATE TABLE IF NOT EXISTS properties (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    address_line VARCHAR(300) NOT NULL,
    postcode VARCHAR(20) NULL,
    city VARCHAR(100) NOT NULL,
    country_code CHAR(2) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    latitude DECIMAL(10,7) NULL,
    longitude DECIMAL(10,7) NULL,
    property_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    bedrooms TINYINT UNSIGNED NOT NULL,
    bathrooms TINYINT UNSIGNED NOT NULL,
    year_built SMALLINT UNSIGNED NULL,
    created_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    version INT NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX ix_properties_creator (created_by_user_id,is_active,created_at,id),
    CONSTRAINT ck_property_address CHECK (CHAR_LENGTH(TRIM(address_line)) BETWEEN 3 AND 300),
    CONSTRAINT ck_property_city CHECK (CHAR_LENGTH(TRIM(city)) BETWEEN 1 AND 100),
    CONSTRAINT ck_property_country CHECK (REGEXP_LIKE(country_code,'^[A-Z]{2}$','c')),
    CONSTRAINT ck_property_type CHECK (property_type IN ('Terraced','SemiDetached','Detached','FlatApartment','Bungalow','Other')),
    CONSTRAINT ck_property_rooms CHECK (bedrooms <= 100 AND bathrooms <= 100),
    CONSTRAINT ck_property_year CHECK (year_built IS NULL OR year_built BETWEEN 1 AND 9999),
    CONSTRAINT ck_property_coordinates CHECK ((latitude IS NULL AND longitude IS NULL) OR
        (latitude IS NOT NULL AND longitude IS NOT NULL AND latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)),
    CONSTRAINT ck_property_active CHECK (is_active IN (0,1)),
    CONSTRAINT ck_property_version CHECK (version > 0)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS property_memberships (
    property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ownership_role VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NULL,
    PRIMARY KEY (property_id,user_id),
    INDEX ix_property_memberships_user (user_id,is_active,property_id),
    CONSTRAINT fk_property_membership_property FOREIGN KEY (property_id) REFERENCES properties(id),
    CONSTRAINT ck_property_membership_role CHECK (ownership_role IN ('Owner','Tenant','Landlord','PropertyManager')),
    CONSTRAINT ck_property_membership_active CHECK (is_active IN (0,1))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS property_user_preferences (
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    current_property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    PRIMARY KEY (user_id),
    CONSTRAINT fk_property_preference_property FOREIGN KEY (current_property_id) REFERENCES properties(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS property_membership_history (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    actor_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    from_role VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
    to_role VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
    event_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    occurred_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    INDEX ix_property_membership_history (property_id,occurred_at,id),
    CONSTRAINT fk_property_history_property FOREIGN KEY (property_id) REFERENCES properties(id),
    CONSTRAINT ck_property_history_event CHECK (event_type IN ('Created','RoleChanged','PropertyArchived')),
    CONSTRAINT ck_property_history_from_role CHECK (from_role IS NULL OR from_role IN ('Owner','Tenant','Landlord','PropertyManager')),
    CONSTRAINT ck_property_history_to_role CHECK (to_role IS NULL OR to_role IN ('Owner','Tenant','Landlord','PropertyManager'))
) ENGINE=InnoDB;
-- The application's existing MySQL account needs SELECT, INSERT and UPDATE on this schema.
-- A DBA must grant access to the actual account/host binding; no credentials/grants are embedded.
-- User IDs reference Identity externally: no cross-schema foreign keys, joins or automatic claims by address.
-- Archive sets is_active=0; no physical DELETE or cascading removal of order/history records.
