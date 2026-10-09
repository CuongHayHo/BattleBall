-- BattleBallServer: drop table (owner: "làm theo đề nghị", 2026-10-07). PROPOSED values; every number is editable on the admin page.
-- Depends on V004 (material_template), V008 (book materials), V009..V012 (dungeons, bosses, mob tiers).
--
-- How the server picks the rule of a kill:
--   boss with role NORMAL -> source_class BOSS, tier = the dungeon tier;  role MINI -> MINI;  role FINAL -> FINAL
--   small mob (role MINION) -> MOB or ELITE by dungeon_boss.mob_tier (or boss_group.mob_tier)
-- Gold is not a row here: it comes from balance_config 'gold_split' (40% mobs / 60% bosses of the per-match gold, elite weight 5).
-- Equipment never drops from small mobs (2,000-item bag limit); book materials drop from bosses only; item fragments are bought with gold (GDD 6).

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE IF NOT EXISTS drop_rule (
  rule_id              INT UNSIGNED NOT NULL AUTO_INCREMENT,
  source_class         ENUM('MOB','ELITE','BOSS','MINI','FINAL') NOT NULL,
  tier                 ENUM('ANY','early','mid','late') NOT NULL DEFAULT 'ANY',   -- dungeon tier; ANY = every tier
  material_template_id INT UNSIGNED NOT NULL,
  rate_pct             DECIMAL(7,3) NOT NULL,                                      -- chance per kill, before the dungeon multiplier (max 100 after it)
  qty_min              INT UNSIGNED NOT NULL DEFAULT 1,
  qty_max              INT UNSIGNED NOT NULL DEFAULT 1,
  enabled              TINYINT(1)   NOT NULL DEFAULT 1,
  note                 VARCHAR(255) NULL,
  updated_at           TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by           VARCHAR(64)  NULL,
  PRIMARY KEY (rule_id),
  UNIQUE KEY uq_drop_rule (source_class, tier, material_template_id),
  CONSTRAINT fk_drop_material FOREIGN KEY (material_template_id) REFERENCES material_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Multiplier of a material's rate in one dungeon (default 1 when no row).
CREATE TABLE IF NOT EXISTS dungeon_drop_mult (
  dungeon_id           INT UNSIGNED NOT NULL,
  material_template_id INT UNSIGNED NOT NULL,
  mult                 DECIMAL(6,2) NOT NULL,
  updated_at           TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by           VARCHAR(64)  NULL,
  PRIMARY KEY (dungeon_id, material_template_id),
  CONSTRAINT fk_ddm_dungeon  FOREIGN KEY (dungeon_id)           REFERENCES dungeon_template (dungeon_id) ON DELETE CASCADE,
  CONSTRAINT fk_ddm_material FOREIGN KEY (material_template_id) REFERENCES material_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;

-- Book materials: 301 old book, 302 paper punch, 303 seal. Rates start from the proposed values of drop_picker.html (low), bosses only.
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by) VALUES
  ('BOSS',  'early', 301, 4,  1, 2, 'Cuốn sách cũ', 'proposal'),
  ('BOSS',  'mid',   301, 5,  1, 2, NULL, 'proposal'),
  ('BOSS',  'late',  301, 6,  1, 3, NULL, 'proposal'),
  ('MINI',  'ANY',   301, 5,  1, 2, NULL, 'proposal'),
  ('FINAL', 'ANY',   301, 10, 2, 4, NULL, 'proposal'),
  ('BOSS',  'early', 302, 2,  1, 1, 'Kìm bấm giấy', 'proposal'),
  ('BOSS',  'mid',   302, 3,  1, 1, NULL, 'proposal'),
  ('BOSS',  'late',  302, 4,  1, 1, NULL, 'proposal'),
  ('MINI',  'ANY',   302, 3,  1, 1, NULL, 'proposal'),
  ('FINAL', 'ANY',   302, 8,  1, 2, NULL, 'proposal'),
  ('FINAL', 'ANY',   303, 2,  1, 1, 'Con dấu, rất hiếm', 'proposal');

-- Elite mobs (including the Cell juniors): a little upgrade stone or stat stone, stackable so the bag limit is not touched.
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by) VALUES
  ('ELITE', 'ANY', 201, 0.5, 1, 1, 'Đá nâng cấp', 'proposal'),
  ('ELITE', 'ANY', 202, 0.5, 1, 1, 'Đá chỉ số', 'proposal');

-- Dungeon 3 "Farm Sách tuyệt kỹ": book materials drop 3 times as often as elsewhere.
INSERT INTO dungeon_drop_mult (dungeon_id, material_template_id, mult, updated_by) VALUES
  (3, 301, 3, 'proposal'), (3, 302, 3, 'proposal'), (3, 303, 3, 'proposal');

-- Tuning knobs that are not drops.
INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('gold_split', '{"mob_share":0.4,"boss_share":0.6,"elite_weight":5}', 'Vàng mỗi trận chia 40% cho quái nhỏ, 60% cho boss; mob elite nhiều gấp 5 lần mob thường', 'proposal'),
  ('minion_cap_per_match', '20', 'Số quái nhỏ elite của một boss (Xên con) tối đa mỗi trận, chống cày vô hạn', 'proposal')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

-- The last real boss of every dungeon is its final boss (proposal; the owner can change roles on the admin page).
UPDATE dungeon_boss x
  JOIN (SELECT dungeon_id, MAX(sort_order) AS mx FROM dungeon_boss WHERE role <> 'MINION' GROUP BY dungeon_id) m
    ON m.dungeon_id = x.dungeon_id AND m.mx = x.sort_order
  SET x.role = 'FINAL', x.updated_by = 'proposal'
  WHERE x.role = 'NORMAL'
    AND x.dungeon_id NOT IN (SELECT dungeon_id FROM (SELECT dungeon_id FROM dungeon_boss WHERE role = 'FINAL') f);

INSERT INTO static_data_version (domain, version, updated_by) VALUES ('DROP', 1, 'proposal')
  ON DUPLICATE KEY UPDATE version = version + 1, updated_by = 'proposal';
UPDATE static_data_version SET version = version + 1, updated_by = 'proposal' WHERE domain = 'DUNGEON';
