-- Run MANUALLY on MySQL 8.0.16+ AFTER 001 and 002. No SQL Server syntax or cross-service joins.
-- Back up the schema/data first and schedule the ALTER for a suitable maintenance window.
-- This migration is ONE-TIME: do not rerun if these columns already exist.
USE zumaris_service_request;
ALTER TABLE service_requests
    ADD COLUMN request_number VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin
        GENERATED ALWAYS AS (CONCAT('SR-', REPLACE(id, '-', ''))) STORED,
    ADD COLUMN service_name VARCHAR(200) NULL,
    ADD COLUMN category_name VARCHAR(200) NULL,
    ADD COLUMN category_icon VARCHAR(700) NULL,
    ADD COLUMN service_area VARCHAR(200) NULL,
    ADD COLUMN technician_name VARCHAR(200) NULL,
    ADD COLUMN property_address VARCHAR(1000) NULL,
    ADD COLUMN service_date DATETIME(6) NULL COMMENT 'Actual service date in UTC; not the preferred appointment',
    ADD COLUMN payment_status VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unpaid',
    ADD COLUMN total_amount DECIMAL(18,2) NULL,
    ADD COLUMN currency_code VARCHAR(3) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD CONSTRAINT ck_service_requests_payment CHECK (payment_status IN ('Unpaid','Pending','Paid','Refunded','Failed')),
    ADD CONSTRAINT ck_service_requests_amount CHECK (total_amount IS NULL OR (total_amount >= 0 AND currency_code IS NOT NULL)),
    ADD CONSTRAINT ck_service_requests_currency CHECK (currency_code IS NULL OR REGEXP_LIKE(currency_code, '^[A-Z]{3}$', 'c')),
    ADD UNIQUE KEY uq_service_requests_number (request_number),
    ADD INDEX ix_service_requests_user_payment (user_id,payment_status,created_at,id),
    ADD INDEX ix_service_requests_user_status (user_id,status,created_at,id);
-- No duplicated history table. Snapshot names/address stay NULL until populated by a trusted integration.
-- Existing payment status defaults to Unpaid; this is NOT reconciliation of historical payment data.
-- Verify/reconcile payment state with authoritative finance data before enabling financial reporting.
-- Amount/currency remain unknown (NULL), never fabricated or accepted from the history API.
-- Completed/cancelled timestamps and cancellation reason are read from existing status history.
-- The current app account only needs its existing SELECT permissions for these GET endpoints.
