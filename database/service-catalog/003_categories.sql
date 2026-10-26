-- Execute manually as administrator. No automatic migration or write API.
-- Runtime account requires SELECT on this table only.
CREATE DATABASE IF NOT EXISTS zumaris_service_catalog CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_service_catalog;
CREATE TABLE IF NOT EXISTS categories (
    id INT NOT NULL,
    name VARCHAR(100) NOT NULL,
    icon VARCHAR(200) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_categories_name (name)
) ENGINE=InnoDB;
-- Preserve existing rows on rerun; inspect final SELECT for conflicts.
INSERT INTO categories (id, name, icon) VALUES
(1, 'Plumbing', '1.png'),
(2, 'Electrical', '2.png'),
(3, 'Heating', '3.png'),
(4, 'Gas', '4.png'),
(5, 'Drainage', '5.png'),
(6, 'Appliances', '6.png'),
(7, 'Carpentry / Handyman', '7.png'),
(8, 'Painting & Decorating', '8.png'),
(9, 'Roofing', '9.png'),
(10, 'Locksmith', '10.png'),
(11, 'Glazing / Windows', '11.png'),
(12, 'Pest Control', '12.png'),
(13, 'Cleaning', '13.png'),
(14, 'Landscaping / Garden', '14.png'),
(15, 'Insulation & Ventilation', '15.png'),
(16, 'Smart Home', '16.png'),
(17, 'Other', '17.png')
ON DUPLICATE KEY UPDATE id = categories.id;
SELECT id, name, icon FROM categories ORDER BY id;
