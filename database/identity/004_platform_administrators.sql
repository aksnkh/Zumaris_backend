-- Run manually as root/admin in the Identity schema; safe unchanged rerun.
USE zumaris_identity;
CREATE TABLE IF NOT EXISTS platform_administrators (
 user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 is_active BOOLEAN NOT NULL DEFAULT TRUE,
 granted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB;
-- Verify the REAL Identity user exists before granting this platform-wide role.
-- INSERT INTO platform_administrators(user_id) VALUES ('REAL-IDENTITY-USER-GUID');
-- Never grant this through registration, profile bodies or untrusted JWT fields.
-- Revoke immediately with UPDATE ... SET is_active=0 WHERE user_id='...'.
