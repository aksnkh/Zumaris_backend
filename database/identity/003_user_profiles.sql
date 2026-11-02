-- Run MANUALLY as root/admin in the existing Identity database (MySQL 8.0.16+).
-- No existing users, passwords, emails, sessions or stored procedures are modified.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS identity_user_profiles (
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    first_name VARCHAR(100) NULL,
    last_name VARCHAR(100) NULL,
    phone VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    photo_path VARCHAR(700) NULL,
    updated_at DATETIME(6) NULL,
    PRIMARY KEY (user_id),
    CONSTRAINT ck_identity_profile_first_name CHECK (first_name IS NULL OR CHAR_LENGTH(TRIM(first_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_identity_profile_last_name CHECK (last_name IS NULL OR CHAR_LENGTH(TRIM(last_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_identity_profile_phone CHECK (phone IS NULL OR REGEXP_LIKE(phone, '^[+][1-9][0-9]{6,14}$', 'c'))
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- User existence/active status is checked while locking identity_users in the same transaction.
-- No FK assumes or changes the legacy identity_users.id character-set/collation.
-- Runtime account requires SELECT on identity_users and SELECT/INSERT/UPDATE on identity_user_profiles.
-- IF NOT EXISTS supports an unchanged rerun; it does not repair incompatible existing tables.
SHOW COLUMNS FROM identity_user_profiles;
