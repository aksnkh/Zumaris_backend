-- Run manually after 001; no user data is deleted or auto-verified.
USE zumaris_provider;
CREATE TABLE IF NOT EXISTS provider_onboarding (
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 revision INT NOT NULL DEFAULT 1,
 reviewed_revision INT NULL,
 reviewed_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 reviewed_at DATETIME(6) NULL,
 review_reason VARCHAR(1000) NULL,
 FOREIGN KEY(provider_id) REFERENCES providers(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS provider_documents (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 kind VARCHAR(80) NOT NULL,
 file_name VARCHAR(200) NOT NULL,
 content_type VARCHAR(80) NOT NULL,
 content MEDIUMBLOB NOT NULL,
 created_at DATETIME(6) NOT NULL,
 FOREIGN KEY(provider_id) REFERENCES providers(id),
 INDEX ix_document_provider(provider_id,created_at,id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS provider_reviews (
 request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 customer_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 rating TINYINT NOT NULL,
 comment VARCHAR(2000) NOT NULL,
 created_at DATETIME(6) NOT NULL,
 FOREIGN KEY(provider_id) REFERENCES providers(id),
 CONSTRAINT ck_review_rating CHECK(rating BETWEEN 1 AND 5),
 INDEX ix_review_provider(provider_id,created_at,request_id)
) ENGINE=InnoDB;
-- category qualifications are self-declared specialties, NOT administrator approvals.
-- Only profile/category APIs set is_qualified; users cannot set is_verified/is_active.
-- Existing verified profiles need document upload and administrator review to use new eligibility.
-- Runtime: SELECT/INSERT/UPDATE. Do not grant schema-changing permissions to runtime.

CREATE TABLE IF NOT EXISTS provider_verification_history (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 revision INT NOT NULL,
 administrator_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 approved BOOLEAN NOT NULL,
 reason VARCHAR(1000) NULL,
 occurred_at DATETIME(6) NOT NULL,
 FOREIGN KEY(provider_id) REFERENCES providers(id),
 INDEX ix_verification_provider(provider_id,occurred_at,id)
) ENGINE=InnoDB;
