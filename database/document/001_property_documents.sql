-- Run manually on MySQL 8.0.16+. File bytes live outside the web root; back up files and metadata together.
CREATE DATABASE IF NOT EXISTS zumaris_document CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_document;
CREATE TABLE IF NOT EXISTS property_documents (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_type VARCHAR(32) NOT NULL,equipment_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
 details JSON NOT NULL,file_name VARCHAR(200) NOT NULL,content_type VARCHAR(64) NOT NULL,size_bytes BIGINT NOT NULL,sha256 CHAR(64) NOT NULL,
 uploaded_at DATETIME(6) NOT NULL,updated_at DATETIME(6) NULL,is_archived BOOLEAN NOT NULL DEFAULT 0,
 INDEX ix_documents_property(property_id,is_archived,uploaded_at,id),INDEX ix_documents_equipment(property_id,equipment_id),INDEX ix_documents_request(property_id,request_id),
 CONSTRAINT ck_document_size CHECK(size_bytes BETWEEN 1 AND 20971520),
 CONSTRAINT ck_document_type CHECK(document_type IN ('Certificate','Invoice','Report','Warranty','Manual','Insurance','FloorPlan','Other')),
 CONSTRAINT ck_document_mime CHECK(content_type IN ('application/pdf','image/jpeg','image/png'))
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS document_events (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 document_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,event_type VARCHAR(40) NOT NULL,title VARCHAR(200) NOT NULL,occurred_at DATETIME(6) NOT NULL,
 INDEX ix_document_events(property_id,occurred_at,id),FOREIGN KEY(document_id) REFERENCES property_documents(id)
) ENGINE=InnoDB;
-- Grant SELECT/INSERT/UPDATE to the real application account separately. No cross-schema SQL or grants are executed here.
