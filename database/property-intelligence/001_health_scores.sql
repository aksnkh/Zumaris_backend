-- Manual additive migration for MySQL 8.0.16+. No cross-schema SQL or automatic startup migration.
CREATE DATABASE IF NOT EXISTS zumaris_property_intelligence CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_property_intelligence;
CREATE TABLE IF NOT EXISTS health_assessments (
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 assessment JSON NOT NULL,updated_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 updated_at DATETIME(6) NOT NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS health_assessment_history (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,assessment JSON NOT NULL,
 actor_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,occurred_at DATETIME(6) NOT NULL,
 INDEX ix_health_assessment_history(property_id,occurred_at,id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS health_score_calculations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 property_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,method_version VARCHAR(20) NOT NULL,
 result JSON NOT NULL,actor_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 calculated_at DATETIME(6) NOT NULL,INDEX ix_health_calculations(property_id,calculated_at,id)
) ENGINE=InnoDB;
-- Application account needs SELECT/INSERT/UPDATE on this schema only. Grant separately to the actual account/host.
-- Assessments are self-reported. No official safety certification or payment state is stored here.
