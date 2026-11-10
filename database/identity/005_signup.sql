-- HISTORICAL SHARED-ACCOUNT SCHEMA. Superseded by 008_reset_independent_accounts.sql.
-- DO NOT APPLY this historical script after the independent-account reset.
-- Run manually AFTER existing Identity setup and 003_user_profiles.sql.
-- Existing endpoints/procedures are unchanged. No account is seeded or auto-verified.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS identity_signup_emails (
 normalized_email VARCHAR(254) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL PRIMARY KEY,
 user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 UNIQUE KEY uq_signup_user(user_id)
) ENGINE=InnoDB;
-- Signup creates identity_users, identity_user_profiles and this reservation atomically.
-- identity_users uses its existing schema (same columns as Google registration).
-- Runtime requires SELECT/INSERT on this table, INSERT on identity_users and
-- SELECT/INSERT on identity_user_profiles, in addition to existing session permissions.
-- Legacy account collisions are checked in identity_users. Existing duplicate emails
-- are never implicitly merged; ambiguous email logins must use the existing username.
