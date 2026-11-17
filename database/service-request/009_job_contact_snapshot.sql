-- Manual MySQL 8.0.16+ migration after 001..008. Stop ServiceRequest during migration/publish.
-- Additive and repeatable; no requests/quotes are deleted, no cross-schema reads are performed.
USE zumaris_service_request;
SELECT GROUP_CONCAT(c.ddl ORDER BY c.seq SEPARATOR ', ') INTO @job_contact_columns
FROM (
 SELECT 1 AS seq,'job_latitude' AS col,'ADD COLUMN job_latitude DECIMAL(10,7) NULL' AS ddl
 UNION ALL SELECT 2,'job_longitude','ADD COLUMN job_longitude DECIMAL(10,7) NULL'
 UNION ALL SELECT 3,'job_customer_name','ADD COLUMN job_customer_name VARCHAR(201) NULL'
 UNION ALL SELECT 4,'job_customer_phone','ADD COLUMN job_customer_phone VARCHAR(40) NULL'
 UNION ALL SELECT 5,'job_contact_captured_at','ADD COLUMN job_contact_captured_at DATETIME(6) NULL'
) c WHERE NOT EXISTS(SELECT 1 FROM information_schema.columns x WHERE x.table_schema=DATABASE() AND x.table_name='service_requests' AND x.column_name=c.col);
SET @job_contact_ddl=IF(@job_contact_columns IS NULL,'SELECT ''Job contact columns already exist.'' AS migration_status',CONCAT('ALTER TABLE service_requests ',@job_contact_columns));
PREPARE job_contact_statement FROM @job_contact_ddl;
EXECUTE job_contact_statement;
DEALLOCATE PREPARE job_contact_statement;
SET @job_contact_ddl=IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='service_requests' AND constraint_name='ck_request_job_coordinates'),
 'SELECT ''Job coordinate check already exists.'' AS migration_status',
 'ALTER TABLE service_requests ADD CONSTRAINT ck_request_job_coordinates CHECK ((job_latitude IS NULL AND job_longitude IS NULL) OR (job_latitude IS NOT NULL AND job_longitude IS NOT NULL AND job_latitude BETWEEN -90 AND 90 AND job_longitude BETWEEN -180 AND 180))');
PREPARE job_contact_statement FROM @job_contact_ddl;
EXECUTE job_contact_statement;
DEALLOCATE PREPARE job_contact_statement;
-- Existing rows remain NULL. No legacy backfill is required before launch.
-- New acceptance stores the snapshot atomically with assignment. Later profile/location edits do not change it.
