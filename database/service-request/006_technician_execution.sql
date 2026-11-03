-- Run manually after 001-005. No data deletion and no cross-service SQL.
USE zumaris_service_request;
CREATE TABLE IF NOT EXISTS service_request_execution (
 request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 technician_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 started_at DATETIME(6) NOT NULL,
 completed_at DATETIME(6) NULL,
 work_report TEXT NULL,
 FOREIGN KEY(request_id) REFERENCES service_requests(id),
 CONSTRAINT ck_execution_completion CHECK((completed_at IS NULL AND work_report IS NULL) OR (completed_at IS NOT NULL AND CHAR_LENGTH(TRIM(work_report))>0))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS service_request_result_photos (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 file_path VARCHAR(700) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 created_at DATETIME(6) NOT NULL,
 FOREIGN KEY(request_id) REFERENCES service_request_execution(request_id),
 INDEX ix_result_request(request_id,id)
) ENGINE=InnoDB;
-- New start/complete transitions are transactional and record existing status history.
-- Completion finalizes the accepted fixed price as the amount due, not a payment.
-- Runtime requires SELECT/INSERT/UPDATE only; uploads use existing private Storage:RootPath.
