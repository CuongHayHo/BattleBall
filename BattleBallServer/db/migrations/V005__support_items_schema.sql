-- BattleBallServer: permanent support items (pet, training armor, flag/wing/weapon, skill book). MariaDB 10.4+/10.6+.
-- Status: PROPOSED design, approved by the owner on 2026-10-07 ("toi duyet"); see docs/database/BattleBall.md 10.21 and 10.22.
-- Depends on V001 (stat_type), V003 (game_character, character_equipment). Data is loaded by V006.
--
-- Each of the 4 support slots of a character holds exactly one kind of item:
--   SUPPORT_1 pet, SUPPORT_2 training armor, SUPPORT_3 flag/wing/weapon, SUPPORT_4 skill book.
-- support_id equals the NRO item_template.id of the item (like skin ids equal costume ids).

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE IF NOT EXISTS support_kind (
  kind_key   VARCHAR(24) NOT NULL,                  -- PET, TRAIN_ARMOR, FLAG_WING_WEAPON, SKILL_BOOK
  name_vi    VARCHAR(80) NOT NULL,
  slot       ENUM('SUPPORT_1','SUPPORT_2','SUPPORT_3','SUPPORT_4') NOT NULL,
  enabled    TINYINT(1)  NOT NULL DEFAULT 1,
  updated_at TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64) NULL,
  PRIMARY KEY (kind_key),
  UNIQUE KEY uq_support_slot (slot)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS support_item_template (
  support_id  INT UNSIGNED NOT NULL,                -- = NRO item id
  kind_key    VARCHAR(24)  NOT NULL,
  name_vi     VARCHAR(120) NOT NULL,
  icon_id     INT UNSIGNED NULL,                    -- NRO icon id (item icon)
  obtain      ENUM('SHOP','CRAFT','DROP','NONE') NOT NULL DEFAULT 'SHOP',
  gem_price   INT UNSIGNED NULL,                    -- only when obtain = SHOP
  enabled     TINYINT(1)   NOT NULL DEFAULT 1,
  note        VARCHAR(255) NULL,
  version     INT UNSIGNED NOT NULL DEFAULT 1,
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (support_id),
  KEY idx_support_kind (kind_key, enabled),
  CONSTRAINT fk_support_kind FOREIGN KEY (kind_key) REFERENCES support_kind (kind_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Fixed stats of an item (flag/wing/weapon). The server applies a row only while stat_type.enabled = 1.
CREATE TABLE IF NOT EXISTS support_item_stat (
  support_id INT UNSIGNED  NOT NULL,
  stat_key   VARCHAR(40)   NOT NULL,
  `value`    DECIMAL(12,2) NOT NULL,
  updated_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64)   NULL,
  PRIMARY KEY (support_id, stat_key),
  CONSTRAINT fk_sistat_item FOREIGN KEY (support_id) REFERENCES support_item_template (support_id) ON DELETE CASCADE,
  CONSTRAINT fk_sistat_stat FOREIGN KEY (stat_key)   REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pet: every interval_s seconds the owner gets +value of stat_key for duration_s seconds.
CREATE TABLE IF NOT EXISTS support_pet_effect (
  support_id  INT UNSIGNED  NOT NULL,
  stat_key    VARCHAR(40)   NOT NULL,
  `value`     DECIMAL(12,2) NOT NULL,
  interval_s  SMALLINT UNSIGNED NOT NULL DEFAULT 15,
  duration_s  SMALLINT UNSIGNED NOT NULL,
  updated_at  TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)   NULL,
  PRIMARY KEY (support_id),
  CONSTRAINT fk_spet_item FOREIGN KEY (support_id) REFERENCES support_item_template (support_id) ON DELETE CASCADE,
  CONSTRAINT fk_spet_stat FOREIGN KEY (stat_key)   REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Training armor: worn = ATK -atk_penalty_pct; each completed match worn adds points_per_match points (cap max_points);
-- not worn = ATK +atk_bonus_pct for as many matches as the stored points, each match uses 1 point (PROPOSED, per-match instead of NRO minutes).
CREATE TABLE IF NOT EXISTS support_train_armor (
  support_id       INT UNSIGNED  NOT NULL,
  level            TINYINT UNSIGNED NOT NULL,
  atk_penalty_pct  DECIMAL(5,2)  NOT NULL,
  atk_bonus_pct    DECIMAL(5,2)  NOT NULL,
  points_per_match SMALLINT UNSIGNED NOT NULL DEFAULT 1,
  max_points       SMALLINT UNSIGNED NOT NULL DEFAULT 50,
  updated_at       TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by       VARCHAR(64)   NULL,
  PRIMARY KEY (support_id),
  CONSTRAINT fk_strain_item FOREIGN KEY (support_id) REFERENCES support_item_template (support_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Skill book: when appraised it gets lines_count random lines drawn from skill_book_pool (weighted, no repeated stat on a book).
CREATE TABLE IF NOT EXISTS skill_book_template (
  support_id  INT UNSIGNED NOT NULL,
  lines_count TINYINT UNSIGNED NOT NULL DEFAULT 1,
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (support_id),
  CONSTRAINT fk_sbook_item FOREIGN KEY (support_id) REFERENCES support_item_template (support_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skill_book_pool (
  stat_key   VARCHAR(40)   NOT NULL,
  min_value  DECIMAL(12,2) NOT NULL,
  max_value  DECIMAL(12,2) NOT NULL,
  weight     SMALLINT UNSIGNED NOT NULL DEFAULT 1,
  enabled    TINYINT(1)    NOT NULL DEFAULT 1,
  updated_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64)   NULL,
  PRIMARY KEY (stat_key),
  CONSTRAINT fk_sbpool_stat FOREIGN KEY (stat_key) REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player side: an owned support item (several characters never share one; everything is per character).
CREATE TABLE IF NOT EXISTS character_support_item (
  instance_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  character_id  BIGINT UNSIGNED NOT NULL,
  support_id    INT UNSIGNED    NOT NULL,
  charge_points SMALLINT UNSIGNED NOT NULL DEFAULT 0,   -- training armor points
  appraised     TINYINT(1)      NOT NULL DEFAULT 1,     -- skill book: 0 until its lines are rolled
  created_at    TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (instance_id),
  KEY idx_csi_char (character_id),
  CONSTRAINT fk_csi_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_csi_item FOREIGN KEY (support_id)   REFERENCES support_item_template (support_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Random lines of one skill book instance.
CREATE TABLE IF NOT EXISTS character_support_stat (
  instance_id BIGINT UNSIGNED NOT NULL,
  line_no     TINYINT UNSIGNED NOT NULL,
  stat_key    VARCHAR(40)      NOT NULL,
  `value`     DECIMAL(12,2)    NOT NULL,
  PRIMARY KEY (instance_id, line_no),
  CONSTRAINT fk_css_inst FOREIGN KEY (instance_id) REFERENCES character_support_item (instance_id) ON DELETE CASCADE,
  CONSTRAINT fk_css_stat FOREIGN KEY (stat_key)    REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- A SUPPORT_n slot of character_equipment points at a character_support_item (the server checks that the kind matches the slot).
ALTER TABLE character_equipment ADD COLUMN support_instance_id BIGINT UNSIGNED NULL;
ALTER TABLE character_equipment ADD UNIQUE KEY uq_equip_support (support_instance_id);
ALTER TABLE character_equipment ADD CONSTRAINT fk_equip_support FOREIGN KEY (support_instance_id) REFERENCES character_support_item (instance_id);

SET FOREIGN_KEY_CHECKS = 1;
