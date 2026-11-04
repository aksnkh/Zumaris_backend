-- Run manually after 001_provider_quote_profiles.sql and 002_onboarding_reviews.sql.
USE zumaris_provider;
CREATE TABLE IF NOT EXISTS provider_signup_details (
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 accepted_at DATETIME(6) NOT NULL,
 terms_version VARCHAR(100) NULL,
 privacy_version VARCHAR(100) NULL,
 CONSTRAINT fk_signup_provider FOREIGN KEY(provider_id) REFERENCES providers(id)
) ENGINE=InnoDB;
-- accepted_at records the affirmative combined checkbox on the server in UTC.
-- Versions come from server configuration when supplied; no legal version is invented.
-- A retry preserves original consent time/versions and never auto-verifies identity.
-- Runtime requires SELECT/INSERT/UPDATE on this table in its OWN schema only.
-- Signup does not declare any trade or bypass document verification.
