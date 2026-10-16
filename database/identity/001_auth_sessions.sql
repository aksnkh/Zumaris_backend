-- Run manually against the existing Identity database before deploying this code.
-- MySQL 8.0+, InnoDB. No USE statement: select the intended database yourself.
-- These names must be unused on the first run. IF NOT EXISTS makes an unchanged rerun safe;
-- it does NOT migrate an incompatible pre-existing table. Inspect SHOW CREATE TABLE first.
-- No existing user tables or stored procedures are changed. No credentials or user data are inserted.

CREATE TABLE IF NOT EXISTS zumaris_auth_sessions (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_name VARCHAR(255) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    revoked_at DATETIME(6) NULL,
    revocation_reason VARCHAR(32) NULL,
    PRIMARY KEY (id),
    INDEX ix_zumaris_auth_sessions_user (user_id, revoked_at, expires_at),
    INDEX ix_zumaris_auth_sessions_expiry (expires_at)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS zumaris_auth_refresh_tokens (
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    session_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    used_at DATETIME(6) NULL,
    PRIMARY KEY (token_hash),
    INDEX ix_zumaris_auth_refresh_tokens_session (session_id),
    CONSTRAINT fk_zumaris_auth_refresh_tokens_session FOREIGN KEY (session_id)
        REFERENCES zumaris_auth_sessions (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- All DATETIME values are UTC. The application needs SELECT/INSERT/UPDATE on both tables.
-- User lookup/registration still call the existing sp_identity_get_user_by_username and
-- sp_identity_register_user procedures. No new stored procedures are necessary.
-- Keep consumed hashes for the entire session lifetime: deleting them early defeats reuse detection.
-- Optional maintenance (run separately): DELETE FROM zumaris_auth_sessions
-- WHERE expires_at < UTC_TIMESTAMP(6) - INTERVAL 7 DAY;
-- The FK automatically deletes that session's token history.
