-- Run manually AFTER 001_service_requests.sql in zumaris_service_request (MySQL 8.0.16+).
-- Uses one ALTER statement so a failed ADD does not leave the old check removed.
-- Safe to rerun when the named check exists; this does not modify user/request rows.
USE zumaris_service_request;
ALTER TABLE service_requests
    DROP CHECK ck_service_requests_status,
    ADD CONSTRAINT ck_service_requests_status
        CHECK (status IN ('Submitted', 'Assigned', 'InProgress', 'Completed', 'Cancelled'));

-- New application operations require SELECT, INSERT and UPDATE on this schema.
-- Apply grants separately using the real app account/host binding; no credentials are included.
-- Photo paths stay in service_request_photos from migration 001; no binary column is added.
