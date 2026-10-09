-- BattleBallServer: skill book crafting (owner decision 2026-10-07: skill books are obtained by crafting materials, as in NRO).
-- Status: PROPOSED mechanics copied from the Teamobi2026 source (combine/DoiSachTuyetKy.java, NangCapSachTuyetKy.java); see
-- docs/database/BattleBall.md 10.25. Depends on V004 (material_template), V005 (support_item_template, skill_book_template).
-- Materials drop only at a LOW rate from bosses (the drop table is the next step: OPEN). No material is sold.
--
-- NRO facts used here: the book exists in 3 versions, one per race (book 1: 1044 Earth, 1211 Namek, 1212 Xayda; book 2: 1278, 1279, 1280).
-- Crafting 10 "old books" + 1 "paper punch" gives a book 1 with 20% success (a failed try loses 5 old books and the punch); with the
-- "seal" the success is 100% and there is a 20% chance of 1 to 2 extra lines. Upgrading book 1 to book 2 needs 10 paper punches, 10%
-- success, the punches are lost either way and the book is kept (it only changes version on success).

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL') NOT NULL;

-- A support item may be limited to one race (skill books are race-specific in NRO). The server checks it when equipping.
ALTER TABLE support_item_template ADD COLUMN race ENUM('EARTH','NAMEC','XAYDA','ANY') NOT NULL DEFAULT 'ANY' AFTER kind_key;

CREATE TABLE IF NOT EXISTS craft_recipe (
  recipe_id             INT UNSIGNED NOT NULL,
  name_vi               VARCHAR(120) NOT NULL,
  recipe_type           ENUM('CRAFT','UPGRADE') NOT NULL,   -- CRAFT: materials make a new book; UPGRADE: an owned book changes version
  input_support_id      INT UNSIGNED NULL,                  -- UPGRADE only: the book that is upgraded (kept on failure)
  result_support_id     INT UNSIGNED NOT NULL,
  success_pct           DECIMAL(5,2) NOT NULL,
  boost_material_id     INT UNSIGNED NULL,                  -- optional extra material (the "seal") consumed to boost the try
  boost_success_pct     DECIMAL(5,2) NULL,                  -- success chance when the boost is used
  extra_line_chance_pct DECIMAL(5,2) NOT NULL DEFAULT 0,    -- chance of extra stat lines on the result when boosted
  extra_lines_min       TINYINT UNSIGNED NOT NULL DEFAULT 0,
  extra_lines_max       TINYINT UNSIGNED NOT NULL DEFAULT 0,
  enabled               TINYINT(1)   NOT NULL DEFAULT 1,
  note                  VARCHAR(255) NULL,
  updated_at            TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by            VARCHAR(64)  NULL,
  PRIMARY KEY (recipe_id),
  KEY idx_recipe_result (result_support_id),
  CONSTRAINT fk_recipe_input  FOREIGN KEY (input_support_id)  REFERENCES support_item_template (support_id),
  CONSTRAINT fk_recipe_result FOREIGN KEY (result_support_id) REFERENCES support_item_template (support_id),
  CONSTRAINT fk_recipe_boost  FOREIGN KEY (boost_material_id) REFERENCES material_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS craft_recipe_input (
  recipe_id            INT UNSIGNED NOT NULL,
  material_template_id INT UNSIGNED NOT NULL,
  quantity             INT UNSIGNED NOT NULL,
  lose_on_fail_pct     DECIMAL(5,2) NOT NULL DEFAULT 100,   -- share of the quantity lost when the try fails (consumed in full on success)
  updated_at           TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by           VARCHAR(64)  NULL,
  PRIMARY KEY (recipe_id, material_template_id),
  CONSTRAINT fk_rinput_recipe   FOREIGN KEY (recipe_id)            REFERENCES craft_recipe (recipe_id) ON DELETE CASCADE,
  CONSTRAINT fk_rinput_material FOREIGN KEY (material_template_id) REFERENCES material_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;

-- Materials (NRO item ids 1283, 1285, 1794; icons from item_template). Not sold: they only drop from bosses.
INSERT INTO material_template (template_id, material_key, kind, name_vi, buy_currency, icon_id, note, updated_by) VALUES
  (301, 'old_book',    'BOOK_MATERIAL', 'Cuốn sách cũ',  'NONE', 11569, 'Nguyên liệu ghép sách tuyệt kỹ; chỉ rơi từ boss, tỉ lệ thấp', 'seed'),
  (302, 'paper_punch', 'BOOK_MATERIAL', 'Kìm bấm giấy',  'NONE', 11572, 'Dùng để ghép (1 cái) và nâng cấp (10 cái) sách tuyệt kỹ; chỉ rơi từ boss, tỉ lệ thấp', 'seed'),
  (303, 'seal',        'BOOK_MATERIAL', 'Con dấu',       'NONE', 15412, 'Tăng tỉ lệ ghép sách lên 100% và có thể thêm dòng chỉ số; chỉ rơi từ boss, tỉ lệ rất thấp', 'seed');

-- The race versions of the books (V006 only had the Earth ones).
UPDATE support_item_template SET race = 'EARTH', name_vi = 'Sách tuyệt kỹ 1 (Trái Đất)' WHERE support_id = 1044;
UPDATE support_item_template SET race = 'EARTH', name_vi = 'Sách tuyệt kỹ 2 (Trái Đất)' WHERE support_id = 1278;
INSERT INTO support_item_template (support_id, kind_key, race, name_vi, icon_id, obtain, gem_price, note, updated_by) VALUES
  (1211, 'SKILL_BOOK', 'NAMEC', 'Sách tuyệt kỹ 1 (Namếc)',  11567, 'CRAFT', NULL, 'Ghép từ nguyên liệu (V008)', 'seed'),
  (1212, 'SKILL_BOOK', 'XAYDA', 'Sách tuyệt kỹ 1 (Xayda)',  11565, 'CRAFT', NULL, 'Ghép từ nguyên liệu (V008)', 'seed'),
  (1279, 'SKILL_BOOK', 'NAMEC', 'Sách tuyệt kỹ 2 (Namếc)',  11568, 'CRAFT', NULL, 'Nâng cấp từ sách 1 (V008)', 'seed'),
  (1280, 'SKILL_BOOK', 'XAYDA', 'Sách tuyệt kỹ 2 (Xayda)',  11566, 'CRAFT', NULL, 'Nâng cấp từ sách 1 (V008)', 'seed');
UPDATE support_item_template SET note = 'Ghép từ nguyên liệu (V008)' WHERE support_id = 1044;
UPDATE support_item_template SET note = 'Nâng cấp từ sách 1 (V008)' WHERE support_id = 1278;
INSERT INTO skill_book_template (support_id, lines_count, updated_by) VALUES
  (1211, 1, 'seed'), (1212, 1, 'seed'), (1279, 1, 'seed'), (1280, 1, 'seed');

-- Recipes. CRAFT 1..3 (one per race), UPGRADE 4..6.
INSERT INTO craft_recipe (recipe_id, name_vi, recipe_type, input_support_id, result_support_id, success_pct, boost_material_id, boost_success_pct, extra_line_chance_pct, extra_lines_min, extra_lines_max, note, updated_by) VALUES
  (1, 'Ghép sách tuyệt kỹ 1 (Trái Đất)', 'CRAFT', NULL, 1044, 20, 303, 100, 20, 1, 2, 'NRO: 10 cuốn sách cũ + 1 kìm bấm giấy, 20%; có con dấu: 100% và 20% thêm 1 đến 2 dòng', 'seed'),
  (2, 'Ghép sách tuyệt kỹ 1 (Namếc)',    'CRAFT', NULL, 1211, 20, 303, 100, 20, 1, 2, NULL, 'seed'),
  (3, 'Ghép sách tuyệt kỹ 1 (Xayda)',    'CRAFT', NULL, 1212, 20, 303, 100, 20, 1, 2, NULL, 'seed'),
  (4, 'Nâng cấp sách 1 lên sách 2 (Trái Đất)', 'UPGRADE', 1044, 1278, 10, NULL, NULL, 0, 0, 0, 'NRO: 10 kìm bấm giấy, 10%, mất kìm dù thất bại, giữ sách', 'seed'),
  (5, 'Nâng cấp sách 1 lên sách 2 (Namếc)',    'UPGRADE', 1211, 1279, 10, NULL, NULL, 0, 0, 0, NULL, 'seed'),
  (6, 'Nâng cấp sách 1 lên sách 2 (Xayda)',    'UPGRADE', 1212, 1280, 10, NULL, NULL, 0, 0, 0, NULL, 'seed');

INSERT INTO craft_recipe_input (recipe_id, material_template_id, quantity, lose_on_fail_pct, updated_by) VALUES
  (1, 301, 10, 50, 'seed'), (1, 302, 1, 100, 'seed'),
  (2, 301, 10, 50, 'seed'), (2, 302, 1, 100, 'seed'),
  (3, 301, 10, 50, 'seed'), (3, 302, 1, 100, 'seed'),
  (4, 302, 10, 100, 'seed'), (5, 302, 10, 100, 'seed'), (6, 302, 10, 100, 'seed');

INSERT INTO static_data_version (domain, version, updated_by) VALUES ('SUPPORT', 1, 'seed')
  ON DUPLICATE KEY UPDATE version = version + 1, updated_by = 'seed';
