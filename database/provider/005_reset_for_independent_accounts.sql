-- DESTRUCTIVE OPTIONAL RESET for the explicitly authorized fresh independent-account setup.
-- Run manually in Provider after its existing 001..004 schema scripts.
-- Deletes old Provider profiles, documents/blobs, reviews, consent and verification history.
-- Does not migrate old Identity account IDs or change the Provider schema.
USE zumaris_provider;
SET @zumaris_previous_fk=@@SESSION.FOREIGN_KEY_CHECKS;
SET SESSION FOREIGN_KEY_CHECKS=0;
TRUNCATE TABLE provider_document_review_history;
TRUNCATE TABLE provider_verification_slots;
TRUNCATE TABLE provider_document_reviews;
TRUNCATE TABLE provider_verification_profile;
TRUNCATE TABLE provider_documents;
TRUNCATE TABLE provider_verification_history;
TRUNCATE TABLE provider_reviews;
TRUNCATE TABLE provider_signup_details;
TRUNCATE TABLE provider_onboarding;
TRUNCATE TABLE provider_category_qualifications;
TRUNCATE TABLE providers;
SET SESSION FOREIGN_KEY_CHECKS=@zumaris_previous_fk;
