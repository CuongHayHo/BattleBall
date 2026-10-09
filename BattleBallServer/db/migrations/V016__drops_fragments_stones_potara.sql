-- BattleBallServer: drops of item fragments (#101-105), upgrade stone (#201), stat stone (#202), crystal stone (#203), Potara shard (#900).
-- Owner: "tự đề xuất cho #101 201 202 203 900", 2026-10-07. PROPOSED numbers, all editable on the admin page (Bảng rơi đồ).
--
-- Calibration (docs/database/BattleBall.md 10.30): one match = 4 normal bosses + the final boss (V015); one won match is worth 1,000 x R gold.
-- Drops are a supplement to buying, so together they are worth about 0.4 of a won match per match:
--   fragments ~0.25 win (a fragment costs 0.6 win), stones ~0.17 win (upgrade stone 1.5 wins, stat stone 1 win), crystal stone very rare.
-- Not idempotent for the data part; the schema part is guarded.

SET NAMES utf8mb4;

-- 1) the Potara shard is a material kind of its own: the server adds it to account.potara_fragments (per account), not to material_stack.
ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL','POTARA_SHARD') NOT NULL;
INSERT INTO material_template (template_id, material_key, kind, name_vi, buy_currency, price_fixed, icon_id, note, updated_by) VALUES
  (900, 'potara_shard', 'POTARA_SHARD', 'Mảnh bông tai Potara', 'GOLD', 15000, NULL,
   'Tài khoản cần 100 mảnh; mua tối đa 20 mảnh mỗi ngày (balance_config potara); ngừng rơi khi đã có bông tai', 'proposal');

-- 2) a drop rule may be limited to one dungeon (dungeon_id NULL = every dungeon).
SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'drop_rule' AND COLUMN_NAME = 'dungeon_id');
SET @ddl = IF(@has_col = 0, 'ALTER TABLE drop_rule ADD COLUMN dungeon_id INT UNSIGNED NULL AFTER material_template_id', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @ddl = IF(@has_col = 0, 'ALTER TABLE drop_rule DROP INDEX uq_drop_rule', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @ddl = IF(@has_col = 0, 'ALTER TABLE drop_rule ADD UNIQUE KEY uq_drop_rule (source_class, tier, material_template_id, dungeon_id)', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @ddl = IF(@has_col = 0, 'ALTER TABLE drop_rule ADD CONSTRAINT fk_drop_dungeon FOREIGN KEY (dungeon_id) REFERENCES dungeon_template (dungeon_id)', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 3) item fragments (101 shirt, 102 pants, 103 gloves, 104 boots, 105 ring): bosses only, the same rate for each of the five kinds.
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by)
SELECT c.source_class, c.tier, m.template_id, c.rate_pct, c.qty_min, c.qty_max, 'Mảnh đồ (mỗi loại cùng tỉ lệ)', 'proposal'
FROM (SELECT 'BOSS' AS source_class, 'early' AS tier, 1.0 AS rate_pct, 1 AS qty_min, 1 AS qty_max
      UNION ALL SELECT 'BOSS', 'mid', 1.2, 1, 1 UNION ALL SELECT 'BOSS', 'late', 1.4, 1, 1
      UNION ALL SELECT 'MINI', 'ANY', 1.5, 1, 1 UNION ALL SELECT 'FINAL', 'ANY', 3.0, 1, 2) c
JOIN material_template m ON m.kind = 'FRAGMENT';

-- 4) stones and crystal stone.
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by) VALUES
  ('BOSS',  'early', 201, 0.5, 1, 1, 'Đá nâng cấp', 'proposal'), ('BOSS', 'mid', 201, 0.7, 1, 1, NULL, 'proposal'), ('BOSS', 'late', 201, 1.0, 1, 1, NULL, 'proposal'),
  ('MINI',  'ANY',   201, 1.5, 1, 1, NULL, 'proposal'),         ('FINAL', 'ANY', 201, 3.0, 1, 1, NULL, 'proposal'),
  ('BOSS',  'early', 202, 0.7, 1, 1, 'Đá chỉ số',   'proposal'), ('BOSS', 'mid', 202, 1.0, 1, 1, NULL, 'proposal'), ('BOSS', 'late', 202, 1.3, 1, 1, NULL, 'proposal'),
  ('MINI',  'ANY',   202, 2.0, 1, 1, NULL, 'proposal'),         ('FINAL', 'ANY', 202, 4.0, 1, 1, NULL, 'proposal'),
  ('FINAL', 'early', 203, 0.10, 1, 1, 'Đá pha lê, siêu hiếm: chỉ boss cuối', 'proposal'),
  ('FINAL', 'mid',   203, 0.20, 1, 1, NULL, 'proposal'),
  ('FINAL', 'late',  203, 0.40, 1, 1, NULL, 'proposal');

-- 5) Potara shard: only the final boss of dungeon 11 (Majin Bư 14h, late tier), the GDD's "boss of one dungeon".
INSERT INTO drop_rule (source_class, tier, material_template_id, dungeon_id, rate_pct, qty_min, qty_max, note, updated_by) VALUES
  ('FINAL', 'ANY', 900, 11, 25.0, 1, 3, 'Mảnh bông tai Potara: chỉ phó bản 11; ngừng rơi khi tài khoản đã có bông tai', 'proposal');

INSERT INTO static_data_version (domain, version, updated_by) VALUES ('DROP', 1, 'proposal')
  ON DUPLICATE KEY UPDATE version = version + 1, updated_by = 'proposal';
