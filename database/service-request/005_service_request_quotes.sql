-- Run MANUALLY as admin after 001/002/003 and numeric ID migration 004 when applicable.
USE zumaris_service_request;
CREATE TABLE IF NOT EXISTS service_request_quotes (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    customer_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    provider_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_version INT NOT NULL,
    status VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    labour_amount DECIMAL(10,2) NOT NULL,
    callout_amount DECIMAL(10,2) NOT NULL,
    total_amount DECIMAL(10,2) NOT NULL,
    currency_code CHAR(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    snapshot JSON NOT NULL,
    created_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NULL,
    decided_at DATETIME(6) NULL,
    decision_reason VARCHAR(200) NULL,
    accepted_request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin
        GENERATED ALWAYS AS (CASE WHEN status='Accepted' THEN request_id ELSE NULL END) STORED,
    pending_provider_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin
        GENERATED ALWAYS AS (CASE WHEN status='Pending' THEN provider_id ELSE NULL END) STORED,
    UNIQUE KEY uq_one_accepted_quote (accepted_request_id),
    UNIQUE KEY uq_one_pending_provider_quote (request_id,pending_provider_id),
    INDEX ix_quote_customer_status (customer_id,status,created_at,id),
    INDEX ix_quote_provider_user (provider_user_id,created_at,id),
    INDEX ix_quote_property (customer_id,property_id,created_at,id),
    CONSTRAINT fk_quote_request FOREIGN KEY (request_id) REFERENCES service_requests(id),
    CONSTRAINT ck_quote_status CHECK(status IN ('Pending','Accepted','Declined','Withdrawn','Expired')),
    CONSTRAINT ck_quote_amounts CHECK(labour_amount>=0 AND callout_amount>=0 AND total_amount=labour_amount+callout_amount AND total_amount>0 AND total_amount<=999999.99),
    CONSTRAINT ck_quote_currency CHECK(currency_code='GBP'),
    CONSTRAINT ck_quote_request_version CHECK(request_version>0),
    CONSTRAINT ck_quote_expiry CHECK(expires_at IS NULL OR expires_at>created_at)
) ENGINE=InnoDB;
-- Runtime account needs SELECT/INSERT/UPDATE on quotes and existing request/history DML.
-- No quote becomes a payment. JSON is an immutable server-built display snapshot, not client input.
-- An unchanged rerun is safe; incompatible pre-existing definitions require inspection.
SHOW COLUMNS FROM service_request_quotes;
