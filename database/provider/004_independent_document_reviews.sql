-- Run manually AFTER Provider 001/002/003. MySQL 8.0.16+.
-- No old files are deleted and no legacy overall approval is fabricated as a per-document approval.
USE zumaris_provider;
CREATE TABLE IF NOT EXISTS provider_document_reviews (
 document_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Draft',
 identity_kind VARCHAR(24) NULL,
 CONSTRAINT ck_identity_kind CHECK((document_type='IdentityCheck' AND identity_kind IS NOT NULL AND identity_kind IN ('Passport','DrivingLicence','OtherId')) OR (document_type<>'IdentityCheck' AND identity_kind IS NULL)),
 content_hash BINARY(32) NOT NULL,
 submitted_at DATETIME(6) NULL,
 reviewed_at DATETIME(6) NULL,
 reviewed_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 review_feedback VARCHAR(1000) NULL,
 CONSTRAINT fk_review_document FOREIGN KEY(document_id) REFERENCES provider_documents(id),
 CONSTRAINT fk_document_review_provider FOREIGN KEY(provider_id) REFERENCES providers(id),
 CONSTRAINT ck_review_document_type CHECK(document_type IN ('IdentityCheck','Insurance','Certificate')),
 CONSTRAINT ck_document_review_state CHECK(status IN ('Draft','InReview','Verified','ActionRequired')),
 CONSTRAINT ck_document_review_timestamps CHECK(
  (status='Draft' AND submitted_at IS NULL AND reviewed_at IS NULL AND reviewed_by IS NULL)
  OR (status='InReview' AND submitted_at IS NOT NULL AND reviewed_at IS NULL AND reviewed_by IS NULL)
  OR (status IN ('Verified','ActionRequired') AND submitted_at IS NOT NULL AND reviewed_at IS NOT NULL AND reviewed_by IS NOT NULL)),
 CONSTRAINT ck_document_review_feedback CHECK(status<>'ActionRequired' OR (review_feedback IS NOT NULL AND CHAR_LENGTH(TRIM(review_feedback))>0)),
 INDEX ix_document_reviews_owner(provider_id,document_type,document_id),
 INDEX ix_document_review_queue(status,submitted_at,document_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS provider_verification_slots (
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 current_document_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 verified_document_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 resubmission_reason VARCHAR(1000) NULL,
 PRIMARY KEY(provider_id,document_type),
 CONSTRAINT fk_verification_slot_provider FOREIGN KEY(provider_id) REFERENCES providers(id),
 CONSTRAINT fk_current_verification_document FOREIGN KEY(current_document_id) REFERENCES provider_document_reviews(document_id),
 CONSTRAINT fk_verified_verification_document FOREIGN KEY(verified_document_id) REFERENCES provider_document_reviews(document_id),
 CONSTRAINT ck_verification_slot_type CHECK(document_type IN ('IdentityCheck','Insurance','Certificate'))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS provider_document_review_history (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 event_type VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 actor_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 occurred_at DATETIME(6) NOT NULL,
 feedback VARCHAR(1000) NULL,
 CONSTRAINT fk_document_history_file FOREIGN KEY(document_id) REFERENCES provider_document_reviews(document_id),
 INDEX ix_document_review_history(provider_id,occurred_at,id)
) ENGINE=InnoDB;
-- Current upload and active verified file are separate: a replacement NEVER inherits approval.
-- Previously verified file stays active until its replacement is approved.
-- IdentityCheck, Insurance and Certificate must have active verified files for new work/quotes.
-- Old unclassified provider_documents remain available via existing private download APIs.
-- Runtime needs SELECT/INSERT/UPDATE on the new tables, no DDL or DELETE.

CREATE TABLE IF NOT EXISTS provider_verification_profile (
 provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 postcode VARCHAR(20) NOT NULL,
 other_skills VARCHAR(1000) NULL,
 updated_at DATETIME(6) NOT NULL,
 CONSTRAINT fk_verification_profile_provider FOREIGN KEY(provider_id) REFERENCES providers(id)
) ENGINE=InnoDB;
