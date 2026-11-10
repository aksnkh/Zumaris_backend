-- DESTRUCTIVE FRESH SETUP. The user explicitly authorized deleting old account data.
-- Run MANUALLY as database administrator in zumaris_identity. No data is migrated.
-- This script deletes App/Tech/Admin accounts, sessions, Google links and administrator setup/audit.
-- Do not run 001..007 after this replacement schema. Stored legacy procedures are no longer used.
CREATE DATABASE IF NOT EXISTS zumaris_identity CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_identity;
SET @zumaris_previous_fk = @@SESSION.FOREIGN_KEY_CHECKS;
SET SESSION FOREIGN_KEY_CHECKS=0;
DROP VIEW IF EXISTS identity_account_directory;
DROP PROCEDURE IF EXISTS sp_identity_get_user_by_username;
DROP PROCEDURE IF EXISTS sp_identity_register_user;
DROP TABLE IF EXISTS zumaris_auth_refresh_tokens;
DROP TABLE IF EXISTS zumaris_auth_sessions;
DROP TABLE IF EXISTS identity_external_logins;
DROP TABLE IF EXISTS identity_signup_emails;
DROP TABLE IF EXISTS identity_user_profiles;
DROP TABLE IF EXISTS platform_administrators;
DROP TABLE IF EXISTS identity_administrator_account_creations;
DROP TABLE IF EXISTS identity_administrator_setup;
DROP TABLE IF EXISTS identity_users;
DROP TABLE IF EXISTS app_accounts;
DROP TABLE IF EXISTS tech_accounts;
DROP TABLE IF EXISTS admin_accounts;
SET SESSION FOREIGN_KEY_CHECKS=@zumaris_previous_fk;

CREATE TABLE app_accounts ( id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, user_name VARCHAR(100) NOT NULL UNIQUE, email VARCHAR(254) NOT NULL UNIQUE, password_hash VARCHAR(1024) NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE, is_deleted BOOLEAN NOT NULL DEFAULT FALSE, created_at DATETIME(6) NOT NULL, first_name VARCHAR(100) NULL, last_name VARCHAR(100) NULL, phone VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL, photo_path VARCHAR(700) NULL, updated_at DATETIME(6) NULL, provider_enrollment_requested_at DATETIME(6) NULL, CONSTRAINT ck_app_phone CHECK(phone IS NULL OR REGEXP_LIKE(phone,'^[+][1-9][0-9]{6,14}$','c')), CONSTRAINT ck_app_names CHECK((first_name IS NULL OR CHAR_LENGTH(TRIM(first_name)) BETWEEN 1 AND 100) AND (last_name IS NULL OR CHAR_LENGTH(TRIM(last_name)) BETWEEN 1 AND 100)) ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE tech_accounts ( id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, user_name VARCHAR(100) NOT NULL UNIQUE, email VARCHAR(254) NOT NULL UNIQUE, password_hash VARCHAR(1024) NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE, is_deleted BOOLEAN NOT NULL DEFAULT FALSE, created_at DATETIME(6) NOT NULL, first_name VARCHAR(100) NULL, last_name VARCHAR(100) NULL, phone VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL, photo_path VARCHAR(700) NULL, updated_at DATETIME(6) NULL, provider_enrollment_requested_at DATETIME(6) NULL, CONSTRAINT ck_tech_phone CHECK(phone IS NULL OR REGEXP_LIKE(phone,'^[+][1-9][0-9]{6,14}$','c')), CONSTRAINT ck_tech_names CHECK((first_name IS NULL OR CHAR_LENGTH(TRIM(first_name)) BETWEEN 1 AND 100) AND (last_name IS NULL OR CHAR_LENGTH(TRIM(last_name)) BETWEEN 1 AND 100)) ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE admin_accounts ( id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, user_name VARCHAR(100) NOT NULL UNIQUE, email VARCHAR(254) NOT NULL UNIQUE, password_hash VARCHAR(1024) NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE, is_deleted BOOLEAN NOT NULL DEFAULT FALSE, created_at DATETIME(6) NOT NULL, first_name VARCHAR(100) NULL, last_name VARCHAR(100) NULL, phone VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL, photo_path VARCHAR(700) NULL, updated_at DATETIME(6) NULL, provider_enrollment_requested_at DATETIME(6) NULL, CONSTRAINT ck_admin_phone CHECK(phone IS NULL OR REGEXP_LIKE(phone,'^[+][1-9][0-9]{6,14}$','c')), CONSTRAINT ck_admin_names CHECK((first_name IS NULL OR CHAR_LENGTH(TRIM(first_name)) BETWEEN 1 AND 100) AND (last_name IS NULL OR CHAR_LENGTH(TRIM(last_name)) BETWEEN 1 AND 100)) ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Read-only directory. Account records remain independent; all writes target a fixed account table.
CREATE VIEW identity_account_directory AS SELECT a.*, 'App' AS application FROM app_accounts a UNION ALL SELECT a.*, 'Tech' AS application FROM tech_accounts a UNION ALL SELECT a.*, 'Admin' AS application FROM admin_accounts a;

CREATE TABLE zumaris_auth_sessions ( id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, user_name VARCHAR(100) NOT NULL, application VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL, created_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL, revoked_at DATETIME(6) NULL, revocation_reason VARCHAR(32) NULL, CONSTRAINT ck_account_session_scope CHECK((application IN ('App','Admin') AND provider_id IS NULL) OR (application='Tech' AND provider_id IS NOT NULL)), INDEX ix_account_sessions_user(user_id,application,revoked_at,expires_at) ) ENGINE=InnoDB;
CREATE TABLE zumaris_auth_refresh_tokens ( token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, session_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, created_at DATETIME(6) NOT NULL, used_at DATETIME(6) NULL, CONSTRAINT fk_account_refresh_session FOREIGN KEY(session_id)REFERENCES zumaris_auth_sessions(id)ON DELETE CASCADE ) ENGINE=InnoDB;
CREATE TABLE identity_external_logins ( id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, application VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, provider VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, provider_subject VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, created_at DATETIME(6) NOT NULL, UNIQUE KEY uq_account_google_subject(application,provider,provider_subject), UNIQUE KEY uq_account_google_user(application,user_id,provider), CONSTRAINT ck_external_application CHECK(application IN ('App','Tech','Admin')) ) ENGINE=InnoDB;
CREATE TABLE identity_administrator_setup ( id TINYINT NOT NULL PRIMARY KEY, completed_at DATETIME(6) NULL, first_administrator_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL, CONSTRAINT ck_admin_setup_id CHECK(id=1), CONSTRAINT ck_admin_setup_time CHECK((completed_at IS NULL AND first_administrator_user_id IS NULL) OR (completed_at IS NOT NULL AND first_administrator_user_id IS NOT NULL)) ) ENGINE=InnoDB;
INSERT INTO identity_administrator_setup(id)VALUES(1);
CREATE TABLE identity_administrator_account_creations ( user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY, created_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL, source VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL, created_at DATETIME(6) NOT NULL, CONSTRAINT ck_admin_creation_source CHECK((source='Bootstrap' AND created_by_user_id IS NULL) OR (source='Administrator' AND created_by_user_id IS NOT NULL)) ) ENGINE=InnoDB;
-- Email and username uniqueness is per account table, never across applications.
-- Database users/GRANTs, reference services and filesystem photos are not removed.
