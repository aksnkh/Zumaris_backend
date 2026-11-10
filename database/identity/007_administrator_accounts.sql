-- Run MANUALLY in Identity after 001..006, using a migration/admin account (MySQL 8.0.16+).
-- Does not create any account, grant a role, reset passwords or reopen completed bootstrap.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS identity_administrator_setup (
 id TINYINT NOT NULL PRIMARY KEY,
 completed_at DATETIME(6) NULL,
 first_administrator_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 CONSTRAINT ck_administrator_setup_singleton CHECK(id=1),
 CONSTRAINT ck_administrator_setup_completed CHECK(
  (completed_at IS NULL AND first_administrator_user_id IS NULL)
  OR (completed_at IS NOT NULL AND first_administrator_user_id IS NOT NULL))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS identity_administrator_account_creations (
 user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 created_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 source VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 CONSTRAINT ck_administrator_creation_source CHECK(
  (source='Bootstrap' AND created_by_user_id IS NULL)
  OR (source='Administrator' AND created_by_user_id IS NOT NULL)),
 INDEX ix_administrator_account_creator(created_by_user_id,created_at)
) ENGINE=InnoDB;
-- Do not assume or alter the legacy identity_users.id character set/collation for new FKs.
-- Existing administrator memberships (even inactive ones) close initial bootstrap when first installed.
INSERT IGNORE INTO identity_administrator_setup(id,completed_at,first_administrator_user_id)
 SELECT 1,granted_at,user_id FROM platform_administrators ORDER BY granted_at,user_id LIMIT 1;
INSERT IGNORE INTO identity_administrator_setup(id)VALUES(1);
-- Unchanged reruns preserve the completed marker and all audit records.
-- Runtime needs SELECT/UPDATE on setup; SELECT/INSERT on creations; SELECT/INSERT on platform_administrators,
-- and the existing Identity signup permissions. No runtime DDL is required.
