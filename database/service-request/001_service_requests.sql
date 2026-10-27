-- Manual deployment script. Run with a database administration account on MySQL 8.0.16+.
-- No Identity tables/procedures are changed. Credentials and application grants are not embedded.
CREATE DATABASE IF NOT EXISTS zumaris_service_request
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_service_request;

-- IF NOT EXISTS permits an unchanged rerun; it does not upgrade incompatible existing tables.
CREATE TABLE IF NOT EXISTS service_requests (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    category_id INT NOT NULL,
    area_type_id INT NOT NULL,
    description VARCHAR(500) NOT NULL,
    urgency VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    preferred_date DATE NULL,
    preferred_start_time TIME NULL,
    preferred_end_time TIME NULL,
    time_zone_id VARCHAR(64) NOT NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    technician_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NULL,
    version INT NOT NULL DEFAULT 1,
    idempotency_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    request_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_service_requests_idempotency (user_id, idempotency_key),
    INDEX ix_service_requests_user_created (user_id, created_at, id),
    INDEX ix_service_requests_status_created (status, created_at, id),
    CONSTRAINT ck_service_requests_category CHECK (category_id > 0),
    CONSTRAINT ck_service_requests_area CHECK (area_type_id > 0),
    CONSTRAINT ck_service_requests_description CHECK (CHAR_LENGTH(TRIM(description)) BETWEEN 20 AND 500),
    CONSTRAINT ck_service_requests_urgency CHECK (urgency IN ('Standard', 'Priority', 'Emergency')),
    CONSTRAINT ck_service_requests_status CHECK (status = 'Submitted'),
    CONSTRAINT ck_service_requests_version CHECK (version > 0),
    CONSTRAINT ck_service_requests_schedule CHECK (
        (urgency = 'Emergency' AND preferred_date IS NULL AND preferred_start_time IS NULL AND preferred_end_time IS NULL)
        OR (preferred_date IS NOT NULL AND preferred_start_time IS NOT NULL AND preferred_end_time IS NOT NULL
            AND preferred_start_time >= '00:00:00' AND preferred_end_time < '24:00:00'
            AND preferred_end_time > preferred_start_time)
    )
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS service_request_status_history (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    from_status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
    to_status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    actor_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    occurred_at DATETIME(6) NOT NULL,
    reason VARCHAR(500) NULL,
    PRIMARY KEY (id),
    INDEX ix_service_request_history_request (request_id, occurred_at, id),
    CONSTRAINT fk_service_request_history_request FOREIGN KEY (request_id) REFERENCES service_requests (id)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Metadata only, reserved for the later upload endpoint. No image bytes are stored in MySQL.
CREATE TABLE IF NOT EXISTS service_request_photos (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    file_path VARCHAR(700) NOT NULL,
    slot TINYINT UNSIGNED NOT NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_service_request_photos_slot (request_id, slot),
    CONSTRAINT ck_service_request_photo_slot CHECK (slot BETWEEN 0 AND 7),
    CONSTRAINT fk_service_request_photos_request FOREIGN KEY (request_id) REFERENCES service_requests (id)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- The application account needs SELECT/INSERT on requests/history for this endpoint.
-- A DBA must grant these on this schema to the existing app account using its actual MySQL host binding.
-- No cross-service foreign keys: user/property/category/area IDs are external references.
-- Later status endpoints must explicitly migrate ck_service_requests_status when introducing new statuses.
