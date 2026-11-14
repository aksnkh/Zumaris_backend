-- Manual migration for MySQL 8.0.16+, AFTER 001..006.
-- Stop ServiceRequest writers and deploy the Pending-based API in the same maintenance window.
-- Back up the schema first. No requests, quotes, photos or history events are deleted.
-- MySQL DDL commits implicitly: the transitional check keeps interrupted runs recoverable.
-- Safe to rerun: both checks are replaced atomically, and updates only match the old name.
USE zumaris_service_request;

ALTER TABLE service_requests
    DROP CHECK ck_service_requests_status,
    ADD CONSTRAINT ck_service_requests_status
        CHECK (status IN ('Submitted', 'Pending', 'Assigned', 'InProgress', 'Completed', 'Cancelled'));

START TRANSACTION;
UPDATE service_requests SET status = 'Pending' WHERE status = 'Submitted';
UPDATE service_request_status_history SET from_status = 'Pending' WHERE from_status = 'Submitted';
UPDATE service_request_status_history SET to_status = 'Pending' WHERE to_status = 'Submitted';
COMMIT;

ALTER TABLE service_requests
    DROP CHECK ck_service_requests_status,
    ADD CONSTRAINT ck_service_requests_status
        CHECK (status IN ('Pending', 'Assigned', 'InProgress', 'Completed', 'Cancelled'));

-- Verification: all three counts must be zero before starting the updated API.
SELECT
    (SELECT COUNT(*) FROM service_requests WHERE status = 'Submitted') AS legacy_requests,
    (SELECT COUNT(*) FROM service_request_status_history WHERE from_status = 'Submitted') AS legacy_history_from,
    (SELECT COUNT(*) FROM service_request_status_history WHERE to_status = 'Submitted') AS legacy_history_to;
