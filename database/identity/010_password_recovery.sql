-- Manual additive MySQL 8.0.16+ migration after independent-account schema 008 and capacity migration 009.
-- Never rerun reset 008 on an existing production database. This script deletes no accounts/sessions.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS identity_password_resets (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 application VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 credential_fingerprint CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,expires_at DATETIME(6) NOT NULL,used_at DATETIME(6) NULL,
 protected_payload MEDIUMTEXT NULL,delivery_status VARCHAR(16) NOT NULL DEFAULT 'Queued',
 next_attempt_at DATETIME(6) NOT NULL,attempts INT NOT NULL DEFAULT 0,
 lease_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,lease_until DATETIME(6) NULL,sent_at DATETIME(6) NULL,
 UNIQUE KEY uq_password_reset_token(token_hash),
 INDEX ix_password_reset_account(application,user_id,created_at,id),
 INDEX ix_password_reset_delivery(delivery_status,next_attempt_at,expires_at,id),
 CONSTRAINT ck_password_reset_application CHECK(application IN ('App','Tech','Admin')),
 CONSTRAINT ck_password_reset_delivery CHECK(delivery_status IN ('Queued','Sent','Cancelled')),
 CONSTRAINT ck_password_reset_expiry CHECK(expires_at>created_at)
) ENGINE=InnoDB;
-- Identity runtime account needs SELECT/INSERT/UPDATE. No cross-schema tables or SQL are used.
-- Only token hashes and encrypted pending email payloads are stored; payload is cleared on sent/cancel/expiry.
-- Preserve the private Data Protection key directory across deployments and restarts.
