-- BattleBallServer: static templates for equipment and stackable materials. MariaDB 10.4+/10.6+ (InnoDB, utf8mb4).
-- Status: PROPOSED design, written 2026-10-07 (owner: "tu de xuat"). Values come from the GDD section 6 and from
-- balance_config.item_economy_wins; nothing here invents new balance numbers. Depends on V001 (stat_type) and V003
-- (item_instance, material_stack). See docs/database/BattleBall.md section 10.20.
--
-- Prices are expressed in "wins": 1 win = gold_per_win_per_level x R gold, where R is the recommended level of the zone where
-- the item is bought (GDD 6, balance_config). Example R = 50: 1 win = 50,000 gold. Zones are not designed yet, so the server
-- computes the gold price at purchase time; admin edits price_wins here.
-- Drop tables are NOT part of this file: they need zone/mob/boss templates first (OPEN).

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- Equipment: one template per slot (GDD 6 gives a single base stat per slot at the normal grade).
-- Extra grades can be added later as more rows (tier) without changing the table.
CREATE TABLE IF NOT EXISTS item_template (
  template_id   INT UNSIGNED NOT NULL,
  item_key      VARCHAR(32)  NOT NULL,
  slot          ENUM('ARMOR','PANTS','GLOVES','BOOTS','RING') NOT NULL,
  name_vi       VARCHAR(80)  NOT NULL,
  tier          TINYINT UNSIGNED NOT NULL DEFAULT 1,
  base_stat_key VARCHAR(40)  NOT NULL,                -- main stat (a stat_type key)
  base_value    DECIMAL(12,2) NOT NULL,               -- GDD 6: armor 2500, hp 200000, atk 20000, ki 200000, ring +25 (%)
  pieces_needed SMALLINT UNSIGNED NOT NULL DEFAULT 10, -- fragments to assemble one item (balance_config.item_economy_wins.shards_per_item)
  min_level     TINYINT UNSIGNED NOT NULL DEFAULT 1,
  icon_id       INT UNSIGNED NULL,                    -- no icon assigned yet
  enabled       TINYINT(1)   NOT NULL DEFAULT 1,
  note          VARCHAR(255) NULL,
  updated_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by    VARCHAR(64)  NULL,
  PRIMARY KEY (template_id),
  UNIQUE KEY uq_item_key (item_key),
  UNIQUE KEY uq_item_slot_tier (slot, tier),
  CONSTRAINT fk_itemtpl_stat FOREIGN KEY (base_stat_key) REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Stackable materials: item fragments, upgrade/stat/crystal stones.
CREATE TABLE IF NOT EXISTS material_template (
  template_id      INT UNSIGNED NOT NULL,
  material_key     VARCHAR(32)  NOT NULL,
  kind             ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE') NOT NULL,
  name_vi          VARCHAR(80)  NOT NULL,
  item_template_id INT UNSIGNED NULL,                  -- FRAGMENT only: the item it assembles into
  buy_currency     ENUM('GOLD','GEM','NONE') NOT NULL DEFAULT 'NONE',
  price_wins       DECIMAL(6,2) NULL,                  -- GOLD: price in wins (see header); one fragment = item_buy / shards_per_item
  price_fixed      INT UNSIGNED NULL,                  -- GEM (or fixed gold): flat price
  max_stack        INT UNSIGNED NOT NULL DEFAULT 999999,
  icon_id          INT UNSIGNED NULL,
  enabled          TINYINT(1)   NOT NULL DEFAULT 1,
  note             VARCHAR(255) NULL,
  updated_at       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by       VARCHAR(64)  NULL,
  PRIMARY KEY (template_id),
  UNIQUE KEY uq_material_key (material_key),
  CONSTRAINT fk_mattpl_item FOREIGN KEY (item_template_id) REFERENCES item_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The two player tables from V003 get their foreign keys now that the templates exist.
ALTER TABLE item_instance  ADD CONSTRAINT fk_item_template FOREIGN KEY (template_id) REFERENCES item_template (template_id);
ALTER TABLE material_stack ADD CONSTRAINT fk_mat_template  FOREIGN KEY (template_id) REFERENCES material_template (template_id);

SET FOREIGN_KEY_CHECKS = 1;

INSERT INTO item_template (template_id, item_key, slot, name_vi, base_stat_key, base_value, note, updated_by) VALUES
  (1, 'armor_basic', 'ARMOR', 'Áo',    'FLAT_ARMOR',    2500,   'GDD 6: Áo = Giáp 2.500',            'seed'),
  (2, 'pants_basic', 'PANTS', 'Quần',  'FLAT_HP',       200000, 'GDD 6: Quần = Máu 200.000',         'seed'),
  (3, 'gloves_basic','GLOVES','Găng',  'FLAT_ATK',      20000,  'GDD 6: Găng = Tấn công 20.000',     'seed'),
  (4, 'boots_basic', 'BOOTS', 'Giày',  'FLAT_KI',       200000, 'GDD 6: Giày = Ki 200.000',          'seed'),
  (5, 'ring_basic',  'RING',  'Nhẫn',  'CRIT_DMG_PCT',  25,     'GDD 6: Nhẫn = +25% sát thương chí mạng', 'seed');

-- Fragment price = item_buy (6 wins) / 10 pieces = 0.6 win. Upgrade stone 1.5 wins, stat stone 1 win (balance_config.item_economy_wins).
-- Crystal stone is bought with gems only (GDD 6: exchange 500 gems).
INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, note, updated_by) VALUES
  (101, 'frag_armor',   'FRAGMENT',      'Mảnh Áo',   1, 'GOLD', 0.60, NULL, 'GDD 6: 10 mảnh ghép 1 món; 6 trận thắng mua đủ món', 'seed'),
  (102, 'frag_pants',   'FRAGMENT',      'Mảnh Quần', 2, 'GOLD', 0.60, NULL, NULL, 'seed'),
  (103, 'frag_gloves',  'FRAGMENT',      'Mảnh Găng', 3, 'GOLD', 0.60, NULL, NULL, 'seed'),
  (104, 'frag_boots',   'FRAGMENT',      'Mảnh Giày', 4, 'GOLD', 0.60, NULL, NULL, 'seed'),
  (105, 'frag_ring',    'FRAGMENT',      'Mảnh Nhẫn', 5, 'GOLD', 0.60, NULL, NULL, 'seed'),
  (201, 'stone_upgrade','UPGRADE_STONE', 'Đá nâng cấp',  NULL, 'GOLD', 1.50, NULL, '+5% chỉ số cơ bản mỗi bậc, tối đa 15 bậc', 'seed'),
  (202, 'stone_stat',   'STAT_STONE',    'Đá chỉ số',    NULL, 'GOLD', 1.00, NULL, 'Cộng % chỉ số tổng; luật ở GDD 6', 'seed'),
  (203, 'stone_crystal','CRYSTAL_STONE', 'Đá pha lê',    NULL, 'GEM',  NULL, 500, 'Siêu hiếm; chỉ đổi bằng đá quý', 'seed');
