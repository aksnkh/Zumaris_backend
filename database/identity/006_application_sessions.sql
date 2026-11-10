-- HISTORICAL SHARED-ACCOUNT SCHEMA. Superseded by 008_reset_independent_accounts.sql.
-- DO NOT APPLY this historical script after the independent-account reset.
-- Run manually in zumaris_identity after 001..005, before deploying application-scoped authentication.
-- MySQL 8.0+. Rerunnable; never infer a role for an existing session.
-- Old sessions are revoked intentionally. Accounts/passwords/provider data are not changed.
USE zumaris_identity;
SET @zumaris_ddl = IF(EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='zumaris_auth_sessions' AND column_name='application'),
 'SELECT 1', 'ALTER TABLE zumaris_auth_sessions ADD COLUMN application VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL');
PREPARE zumaris_stmt FROM @zumaris_ddl;
EXECUTE zumaris_stmt;
DEALLOCATE PREPARE zumaris_stmt;
SET @zumaris_ddl = IF(EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='zumaris_auth_sessions' AND column_name='provider_id'),
 'SELECT 1', 'ALTER TABLE zumaris_auth_sessions ADD COLUMN provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL');
PREPARE zumaris_stmt FROM @zumaris_ddl;
EXECUTE zumaris_stmt;
DEALLOCATE PREPARE zumaris_stmt;
SET @zumaris_ddl = IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='zumaris_auth_sessions' AND constraint_name='ck_identity_session_application'),
 'SELECT 1', 'ALTER TABLE zumaris_auth_sessions ADD CONSTRAINT ck_identity_session_application CHECK ((application IS NULL AND provider_id IS NULL) OR (application IS NOT NULL AND application IN (''App'',''Admin'') AND provider_id IS NULL) OR (application IS NOT NULL AND application=''Tech'' AND provider_id IS NOT NULL))');
PREPARE zumaris_stmt FROM @zumaris_ddl;
EXECUTE zumaris_stmt;
DEALLOCATE PREPARE zumaris_stmt;
-- Workbench Safe Updates blocks this intentional non-key migration predicate.
-- Temporarily change only this connection's setting, then restore its original value.
SET @zumaris_previous_safe_updates = @@SESSION.SQL_SAFE_UPDATES;
SET SESSION SQL_SAFE_UPDATES = 0;
UPDATE zumaris_auth_sessions SET revoked_at=UTC_TIMESTAMP(6),revocation_reason='application_scope_required'
WHERE application IS NULL AND revoked_at IS NULL;
SET SESSION SQL_SAFE_UPDATES = @zumaris_previous_safe_updates;
