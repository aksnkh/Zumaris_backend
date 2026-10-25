-- Run manually as the database administrator. Never run automatically at startup.
-- Runtime account needs SELECT only on zumaris_service_catalog.areas.
CREATE DATABASE IF NOT EXISTS zumaris_service_catalog CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_service_catalog;
CREATE TABLE IF NOT EXISTS areas (
    id INT NOT NULL,
    name VARCHAR(100) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_areas_name (name)
) ENGINE=InnoDB;
-- Stable IDs: preserve existing rows on rerun; inspect any conflicting existing data.
INSERT INTO areas (id, name) VALUES
(1, 'Kitchen'),
(2, 'Bathroom'),
(3, 'Ensuite'),
(4, 'Utility room'),
(5, 'Living room'),
(6, 'Dining room'),
(7, 'Bedroom'),
(8, 'Hallway / Landing'),
(9, 'Stairs'),
(10, 'Loft / Attic'),
(11, 'Garage'),
(12, 'Garden / Outdoor'),
(13, 'Driveway'),
(14, 'Roof'),
(15, 'External walls'),
(16, 'Boiler cupboard / Plant room'),
(17, 'Meter cupboard'),
(18, 'Whole property'),
(19, 'Other')
ON DUPLICATE KEY UPDATE id = areas.id;
SELECT id, name FROM areas ORDER BY id;
