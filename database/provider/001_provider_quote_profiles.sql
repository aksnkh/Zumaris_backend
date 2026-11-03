-- Run MANUALLY as admin. No identity records or verification flags are fabricated.
CREATE DATABASE IF NOT EXISTS zumaris_provider CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_provider;
CREATE TABLE IF NOT EXISTS providers (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    display_name VARCHAR(200) NOT NULL,
    photo_url VARCHAR(700) NULL,
    rating DECIMAL(3,2) NULL,
    completed_jobs INT NULL,
    is_verified BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE KEY uq_provider_identity_user (user_id),
    CONSTRAINT ck_provider_name CHECK (CHAR_LENGTH(TRIM(display_name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_provider_rating CHECK (rating IS NULL OR rating BETWEEN 0 AND 5),
    CONSTRAINT ck_provider_jobs CHECK (completed_jobs IS NULL OR completed_jobs>=0)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS provider_category_qualifications (
    provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    category_id INT NOT NULL,
    is_qualified BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY(provider_id,category_id),
    CONSTRAINT fk_provider_category_provider FOREIGN KEY(provider_id) REFERENCES providers(id),
    CONSTRAINT ck_provider_category CHECK(category_id>0)
) ENGINE=InnoDB;
-- For the complete workflow also apply 002_onboarding_reviews.sql.
-- Providers self-declare categories through their profile API; is_qualified represents
-- that declaration, not category approval by administrators. Administrators review
-- identity/documents only, through the revision-checked API. Do not seed fake approvals.
-- No cross-service FK or reads into Identity/ServiceCatalog.
