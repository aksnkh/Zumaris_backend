-- Run once after 008 if it was already applied before email-based usernames. No account data is deleted.
USE zumaris_identity;
ALTER TABLE app_accounts MODIFY user_name VARCHAR(254) NOT NULL;
ALTER TABLE tech_accounts MODIFY user_name VARCHAR(254) NOT NULL;
ALTER TABLE admin_accounts MODIFY user_name VARCHAR(254) NOT NULL;
ALTER TABLE zumaris_auth_sessions MODIFY user_name VARCHAR(254) NOT NULL;
-- Existing usernames, if any, remain unchanged; all new accounts use normalized email.
