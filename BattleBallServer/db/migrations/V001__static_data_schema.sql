-- BattleBallServer: static game data (skills, skins, stats, balance). MariaDB 10.6+ (InnoDB, utf8mb4).
-- Status: PROPOSED design, written 2026-10-07. NOT yet executed against a real MariaDB (none is installed on the
-- author's machine); the matching seed file V002 was checked structurally only (see docs/database/BattleBall.md 10.17).
--
-- Design rules (from the owner's decisions):
--   * Everything an admin may want to change lives in DATA, not in column types: skin kinds, rarities, price per body group
--     and the stat types are lookup tables; a stat type is turned on or off with stat_type.enabled (kích hoạt / không kích hoạt).
--   * Every table carries updated_at / updated_by so the admin page can show who changed what; writes go through the admin page
--     and are mirrored in admin_audit_log; bump static_data_version.version for the domain after each edit batch so the
--     server and the clients know to reload.
--   * Player data (accounts, characters, items...) is NOT here; see docs/database/BattleBall.md section 3.

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ---------------------------------------------------------------------------------------------------------------
-- Infrastructure
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS static_data_version (
  domain      VARCHAR(32)  NOT NULL,                 -- 'STAT', 'SKIN', 'SKILL', 'BALANCE'
  version     INT UNSIGNED NOT NULL DEFAULT 1,
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (domain)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admin_audit_log (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  admin_user  VARCHAR(64)  NOT NULL,
  table_name  VARCHAR(64)  NOT NULL,
  row_key     VARCHAR(128) NOT NULL,
  action      ENUM('INSERT','UPDATE','DELETE') NOT NULL,
  before_json JSON NULL,
  after_json  JSON NULL,
  created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_audit_row (table_name, row_key),
  KEY idx_audit_time (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Free-form tunables (Max_ATK_Total, gold per win, armor constant, Ki regen, Potara prices, boss rhythm...).
CREATE TABLE IF NOT EXISTS balance_config (
  config_key  VARCHAR(64)  NOT NULL,
  value_json  JSON         NOT NULL,
  description VARCHAR(255) NULL,
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (config_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Stat types a skin can carry (49 types, all present; enabled = the admin's on/off switch)
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS stat_type (
  stat_key       VARCHAR(40)  NOT NULL,              -- e.g. ATK_PCT, HP_PCT, CRIT_RATE_PCT, EFFECT_STEALTH
  name_vi        VARCHAR(160) NOT NULL,
  unit           ENUM('PCT','FLAT','FLAG','OTHER') NOT NULL,
  applies        ENUM('YES','EFFECT','NO') NOT NULL, -- YES = plain stat; EFFECT = needs its own mechanic; NO = not applicable (single player)
  enabled        TINYINT(1)   NOT NULL DEFAULT 0,    -- 1 = active (the server applies it and the admin may assign it), 0 = inactive
  suggested_max  DECIMAL(12,2) NULL,                 -- soft cap per skin (warning only)
  sort_order     SMALLINT     NOT NULL DEFAULT 0,
  nro_option_ids JSON         NULL,                  -- NRO item option ids this type replaces (reference)
  nro_code_ref   VARCHAR(255) NULL,
  note           VARCHAR(255) NULL,
  updated_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by     VARCHAR(64)  NULL,
  PRIMARY KEY (stat_key),
  KEY idx_stat_enabled (enabled, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Skins (costumes). Client costume id == NRO item_template.id.
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS skin_kind (
  kind_key             VARCHAR(24) NOT NULL,          -- SHOP, FUSION_FORM, COMPONENT, SKILL_FORM, BOSS_MOB, NPC (admin can add more)
  name_vi              VARCHAR(80) NOT NULL,
  sellable             TINYINT(1)  NOT NULL DEFAULT 0, -- has a gem price / can be bought
  player_wearable      TINYINT(1)  NOT NULL DEFAULT 0, -- a player character may equip it
  requires_fusion_main TINYINT(1)  NOT NULL DEFAULT 0, -- only the main character while fused with the Potara earring; never AI / the secondary
  enabled              TINYINT(1)  NOT NULL DEFAULT 1,
  sort_order           SMALLINT    NOT NULL DEFAULT 0,
  note                 VARCHAR(255) NULL,
  updated_at           TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by           VARCHAR(64) NULL,
  PRIMARY KEY (kind_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_rarity (
  rarity_key       VARCHAR(16) NOT NULL,              -- COMMON, RARE, VERY_RARE
  name_vi          VARCHAR(40) NOT NULL,
  price_mult       DECIMAL(6,2) NOT NULL,             -- gem price = skin_group_price.base_price x price_mult
  default_stat_pct DECIMAL(6,2) NOT NULL,             -- default value for each default stat key of a new skin of this rarity
  sort_order       SMALLINT    NOT NULL DEFAULT 0,
  updated_at       TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by       VARCHAR(64) NULL,
  PRIMARY KEY (rarity_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_group_price (
  body_group VARCHAR(16)  NOT NULL,                   -- Standard, Chibi, Fusion, Broly, GreatApe, Component
  base_price INT UNSIGNED NOT NULL DEFAULT 0,         -- gems
  updated_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64)  NULL,
  PRIMARY KEY (body_group)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_template (
  costume_id      INT UNSIGNED NOT NULL,
  name            VARCHAR(120) NOT NULL,
  gender_id       TINYINT UNSIGNED NOT NULL,          -- 0 Trai Dat, 1 Namec, 2 Xayda, 3 any race (client catalog values)
  gender_name     VARCHAR(24)  NOT NULL,
  body_group      VARCHAR(16)  NOT NULL,
  icon_id         INT UNSIGNED NOT NULL,
  kind_key        VARCHAR(24)  NOT NULL,
  rarity_key      VARCHAR(16)  NULL,                  -- only for sellable kinds
  rarity_status   ENUM('HEURISTIC','MANUAL') NULL,    -- HEURISTIC = proposed by the tool, MANUAL = set by the owner/admin
  gem_price       INT UNSIGNED NULL,                  -- gems; NULL when not sellable
  fusion_race     ENUM('td','xd','nm','ANY') NULL,    -- only for FUSION_FORM: which race's main character may wear it
  ai_can_wear     TINYINT(1)   NOT NULL DEFAULT 0,    -- always 0 for FUSION_FORM (AI and the fused secondary never wear it)
  enabled         TINYINT(1)   NOT NULL DEFAULT 1,
  nro_description VARCHAR(255) NULL,                  -- NRO reference data
  nro_sold        TINYINT(1)   NULL,
  nro_gem_cost    INT UNSIGNED NULL,
  nro_event_item  TINYINT(1)   NULL,
  version         INT UNSIGNED NOT NULL DEFAULT 1,
  updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by      VARCHAR(64)  NULL,
  PRIMARY KEY (costume_id),
  KEY idx_skin_kind (kind_key, enabled),
  CONSTRAINT fk_skin_kind   FOREIGN KEY (kind_key)   REFERENCES skin_kind (kind_key),
  CONSTRAINT fk_skin_rarity FOREIGN KEY (rarity_key) REFERENCES skin_rarity (rarity_key),
  CONSTRAINT fk_skin_group  FOREIGN KEY (body_group) REFERENCES skin_group_price (body_group)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Stats carried by a skin. The server applies a row only while stat_type.enabled = 1.
CREATE TABLE IF NOT EXISTS skin_stat (
  costume_id INT UNSIGNED NOT NULL,
  stat_key   VARCHAR(40)  NOT NULL,
  `value`    DECIMAL(12,2) NOT NULL DEFAULT 0,        -- percent points for PCT, plain number for FLAT, 1 for FLAG
  updated_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64)  NULL,
  PRIMARY KEY (costume_id, stat_key),
  CONSTRAINT fk_skinstat_skin FOREIGN KEY (costume_id) REFERENCES skin_template (costume_id) ON DELETE CASCADE,
  CONSTRAINT fk_skinstat_stat FOREIGN KEY (stat_key)   REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- What NRO itself attached to the costume (reference for the admin page only; not used at runtime).
CREATE TABLE IF NOT EXISTS skin_nro_stat (
  costume_id INT UNSIGNED NOT NULL,
  stat_key   VARCHAR(40)  NOT NULL,
  nro_value  INT          NOT NULL,
  PRIMARY KEY (costume_id, stat_key),
  CONSTRAINT fk_skinnro_skin FOREIGN KEY (costume_id) REFERENCES skin_template (costume_id) ON DELETE CASCADE,
  CONSTRAINT fk_skinnro_stat FOREIGN KEY (stat_key)   REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Skills (NRO skill set; values RAW from NRO except the owner's rules documented in docs/database/BattleBall.md 10.6-10.8)
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS skill_template (
  skill_id        SMALLINT UNSIGNED NOT NULL,         -- NRO skill id (kept)
  race            ENUM('EARTH','NAMEC','XAYDA','COMMON') NOT NULL,
  name            VARCHAR(100) NOT NULL,
  nro_type        TINYINT UNSIGNED NOT NULL,          -- 1 attack on target, 2 buff another player, 3 self, 4 special/ultimate
  kind            ENUM('ATTACK','ALLY_BUFF','SELF','SPECIAL') NOT NULL,
  ki_cost_mode_nro ENUM('ABSOLUTE','PERCENT_OF_MAX','ALL') NOT NULL,
  ki_cost_mode    ENUM('PERCENT_OF_MAX','ALL') NOT NULL, -- BattleBall: every skill costs % of max Ki, or all Ki
  dam_info        VARCHAR(255) NULL,
  damage_meaning  VARCHAR(40)  NOT NULL,
  cast_modes      SET('DIRECTION','TARGET','SELF') NOT NULL,
  hit_shape       ENUM('RECT_FRONT','CIRCLE','TARGET','HALF_CIRCLE_FRONT','NONE') NOT NULL,
  icon_id         INT UNSIGNED NULL,
  slot            TINYINT UNSIGNED NOT NULL,
  nro_max_point   TINYINT UNSIGNED NOT NULL,
  max_level       TINYINT UNSIGNED NOT NULL,          -- 7, ultimates 9
  enabled         TINYINT(1)   NOT NULL DEFAULT 1,
  version         INT UNSIGNED NOT NULL DEFAULT 1,
  updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by      VARCHAR(64)  NULL,
  PRIMARY KEY (skill_id),
  KEY idx_skill_race (race, slot)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skill_level (
  skill_id            SMALLINT UNSIGNED NOT NULL,
  `level`             TINYINT UNSIGNED  NOT NULL,
  nro_level_id        INT UNSIGNED NULL,
  damage_raw          INT          NOT NULL DEFAULT 0, -- NRO "damage" column (meaning depends on skill, see skill_template.damage_meaning)
  damage_pct_atk      DECIMAL(10,2) NULL,              -- % of ATK (attack skills and ultimates 24, 25)
  damage_pct_max_ki   DECIMAL(10,2) NULL,              -- % of max Ki (Makankosappo)
  dx                  INT          NOT NULL DEFAULT 0, -- NRO reach box (reference)
  dy                  INT          NOT NULL DEFAULT 0,
  ki_cost_pct         DECIMAL(8,2) NULL,               -- % of max Ki; NULL when ki_cost_mode = ALL
  ki_cost_nro         INT          NOT NULL DEFAULT 0,
  ki_cost_rule        VARCHAR(120) NULL,
  cooldown_ms         INT UNSIGNED NOT NULL DEFAULT 0,
  max_fight           TINYINT UNSIGNED NULL,
  gold_cost           INT UNSIGNED NULL,               -- gold to learn / upgrade TO this level (gold only)
  gold_cost_status    ENUM('NRO','PROPOSED_RULE','MANUAL') NOT NULL DEFAULT 'PROPOSED_RULE',
  info                VARCHAR(255) NULL,
  params              JSON         NULL,               -- real per-level effect values (stun ms, heal %, range...)
  params_source       VARCHAR(255) NULL,
  length_px           INT          NULL,               -- hit area (NULL = not configured)
  width_px            INT          NULL,
  radius_px           INT          NULL,
  projectile_speed    INT          NULL,               -- px per second
  travel_ms           INT          NULL,
  slack_px            INT          NULL,
  status              VARCHAR(16)  NOT NULL DEFAULT 'NRO_RAW',
  updated_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by          VARCHAR(64)  NULL,
  PRIMARY KEY (skill_id, `level`),
  CONSTRAINT fk_skilllevel_skill FOREIGN KEY (skill_id) REFERENCES skill_template (skill_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Skill-activated appearance (Xayda monkey form of skill 13). Not a skin: never sold, never in skin_template.
CREATE TABLE IF NOT EXISTS skill_form (
  skill_id     SMALLINT UNSIGNED NOT NULL,
  `level`      TINYINT UNSIGNED  NOT NULL,
  head_part_id INT UNSIGNED NOT NULL,
  body_part_id INT UNSIGNED NOT NULL,
  leg_part_id  INT UNSIGNED NOT NULL,
  PRIMARY KEY (skill_id, `level`),
  CONSTRAINT fk_skillform_skill FOREIGN KEY (skill_id) REFERENCES skill_template (skill_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;
