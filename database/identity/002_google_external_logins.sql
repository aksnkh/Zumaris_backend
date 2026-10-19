-- Run MANUALLY in zumaris_identity. password_hash ALTER was already applied by the owner.
-- This file only creates the external-login mapping table. No data or existing passwords are modified.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS identity_external_logins (
    id CHAR(36) COLLATE utf8mb4_unicode_ci NOT NULL,
    user_id CHAR(36) COLLATE utf8mb4_unicode_ci NOT NULL,
    provider VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    provider_subject VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_external_login_subject (provider, provider_subject),
    UNIQUE KEY uq_external_login_user_provider (user_id, provider),
    CONSTRAINT fk_external_login_user FOREIGN KEY (user_id) REFERENCES identity_users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- IF NOT EXISTS does not repair incompatible existing definitions.
