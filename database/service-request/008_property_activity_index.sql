-- Manual MySQL 8.0.16+ migration, after 007_pending_request_status.sql.
-- Additive/repeatable; supports property-scoped service summaries and timeline joins.
USE zumaris_service_request;
SET @passport_index_sql = IF(
 EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='service_requests' AND index_name='ix_requests_property_user'),
 'SELECT ''Property activity index already exists.'' AS migration_status',
 'ALTER TABLE service_requests ADD INDEX ix_requests_property_user (property_id,user_id,created_at,id)');
PREPARE passport_index_statement FROM @passport_index_sql;
EXECUTE passport_index_statement;
DEALLOCATE PREPARE passport_index_statement;
