-- Run manually as root/admin after 001_areas.sql; application never runs DDL.
USE zumaris_service_catalog;
SET @add_icon = IF(
    EXISTS(SELECT 1 FROM information_schema.columns
           WHERE table_schema = DATABASE() AND table_name = 'areas' AND column_name = 'icon'),
    'SELECT 1',
    'ALTER TABLE areas ADD COLUMN icon VARCHAR(200) NULL');
PREPARE area_icon_migration FROM @add_icon;
EXECUTE area_icon_migration;
DEALLOCATE PREPARE area_icon_migration;

-- Agreed filenames: 1.png through 19.png in C:\ZumarisData\AreaIcon.
-- Safe to rerun: only these catalog mappings are set; no image files are created.
UPDATE areas SET icon = CONCAT(id, '.png') WHERE id BETWEEN 1 AND 19;
SELECT id, name, icon FROM areas ORDER BY id;
