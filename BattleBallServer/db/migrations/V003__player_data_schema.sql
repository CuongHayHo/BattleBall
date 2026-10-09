-- BattleBallServer: player data (accounts, characters, wallet, skins, items). MariaDB 10.6+ (InnoDB, utf8mb4).
-- Status: PROPOSED design, written 2026-10-07. NOT yet executed against a real MariaDB (none installed on the author's
-- machine). Depends on V001 (skin_template, skill_template). See docs/database/BattleBall.md section 10.18.
--
-- Scope decision (owner, 2026-10-07): EVERYTHING is per CHARACTER (gold, gems, bag, owned skins, materials, skills).
-- The only account-level data are the login itself and the single Potara earring (it joins two characters of one account,
-- so it cannot belong to one of them; this is an assumption, see 10.18 OPEN).
-- Not designed yet (OPEN): RunProgress, AnomalyLog, ItemLog, minimap bitmasks.

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ---------------------------------------------------------------------------------------------------------------
-- Static: avatars/heads a new character receives (admin editable). Decision 2026-10-07: COMPONENT skins are obtained
-- when the character is created, not bought.
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS race_default_skin (
  race        ENUM('EARTH','NAMEC','XAYDA') NOT NULL,
  costume_id  INT UNSIGNED NOT NULL,
  is_equipped TINYINT(1)   NOT NULL DEFAULT 0,        -- 1 = worn right after creation (exactly one per race)
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (race, costume_id),
  CONSTRAINT fk_rds_skin FOREIGN KEY (costume_id) REFERENCES skin_template (costume_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Account and login
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS account (
  account_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username         VARCHAR(20)  NOT NULL,              -- 4..20 chars a-z 0-9 _ (CONFIRMED)
  password_hash    VARCHAR(72)  NOT NULL,              -- bcrypt cost 12 (60 chars), shared by the PHP web and the Java server
  role             ENUM('PLAYER','ADMIN') NOT NULL DEFAULT 'PLAYER',
  banned           TINYINT(1)   NOT NULL DEFAULT 0,
  potara_owned     TINYINT(1)   NOT NULL DEFAULT 0,    -- one earring per account (CONFIRMED); never consumed
  potara_fragments SMALLINT UNSIGNED NOT NULL DEFAULT 0, -- 100 fragments make the earring; no more once owned
  created_at       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_login_at    TIMESTAMP    NULL,
  PRIMARY KEY (account_id),
  UNIQUE KEY uq_account_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Long-lived login token (sliding 30 days, one per device). Only the hash is stored.
CREATE TABLE IF NOT EXISTS auth_token (
  token_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  account_id  BIGINT UNSIGNED NOT NULL,
  token_hash  CHAR(64)     NOT NULL,                   -- SHA-256 hex of the token
  device_info VARCHAR(160) NULL,
  created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  revoked_at  TIMESTAMP    NULL,
  PRIMARY KEY (token_id),
  UNIQUE KEY uq_token_hash (token_hash),
  KEY idx_token_account (account_id),
  CONSTRAINT fk_token_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Character: 3 per account, one per race, created together with the account
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS game_character (
  character_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  account_id   BIGINT UNSIGNED NOT NULL,
  race         ENUM('EARTH','NAMEC','XAYDA') NOT NULL,
  level        TINYINT UNSIGNED NOT NULL DEFAULT 1,    -- 1..100
  exp          BIGINT UNSIGNED  NOT NULL DEFAULT 0,
  created_at   TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id),
  UNIQUE KEY uq_char_account_race (account_id, race),
  CONSTRAINT fk_char_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS character_wallet (
  character_id BIGINT UNSIGNED NOT NULL,
  gold         BIGINT UNSIGNED NOT NULL DEFAULT 0,
  gems         BIGINT UNSIGNED NOT NULL DEFAULT 0,
  version      INT UNSIGNED    NOT NULL DEFAULT 1,     -- optimistic lock
  updated_at   TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id),
  CONSTRAINT fk_wallet_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Every change of gold/gems with a reason and before/after. idempotency_key makes a retried request a no-op.
CREATE TABLE IF NOT EXISTS currency_ledger (
  ledger_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  character_id     BIGINT UNSIGNED NOT NULL,
  currency         ENUM('GOLD','GEM') NOT NULL,
  delta            BIGINT       NOT NULL,
  balance_before   BIGINT UNSIGNED NOT NULL,
  balance_after    BIGINT UNSIGNED NOT NULL,
  reason           VARCHAR(32)  NOT NULL,              -- MATCH_REWARD, BUY_SKIN, UPGRADE_SKILL, SELL_ITEM, REVIVE...
  ref              VARCHAR(64)  NULL,
  idempotency_key  VARCHAR(64)  NULL,
  created_at       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (ledger_id),
  UNIQUE KEY uq_ledger_idem (character_id, idempotency_key),
  KEY idx_ledger_char_time (character_id, created_at),
  CONSTRAINT fk_ledger_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS character_skill (
  character_id BIGINT UNSIGNED NOT NULL,
  skill_id     SMALLINT UNSIGNED NOT NULL,              -- skill_template.skill_id (type matches V001)
  level        TINYINT UNSIGNED NOT NULL DEFAULT 1,     -- 1..max_level of the skill (7, ultimates 9)
  updated_at   TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id, skill_id),
  CONSTRAINT fk_cskill_char  FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_cskill_skill FOREIGN KEY (skill_id)     REFERENCES skill_template (skill_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Skins owned by a character (skin id == costume id). Race lock is checked by the server against skin_template.gender_id.
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS owned_skin (
  character_id BIGINT UNSIGNED NOT NULL,
  costume_id   INT UNSIGNED    NOT NULL,
  source       ENUM('PURCHASE','DEFAULT','REWARD','ADMIN') NOT NULL,
  acquired_at  TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id, costume_id),
  CONSTRAINT fk_oskin_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_oskin_skin FOREIGN KEY (costume_id)   REFERENCES skin_template (costume_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------------------------------------------------
-- Items. template_id gets its foreign key to item_template in V004 (item_template is created there).
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_instance (
  item_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  character_id    BIGINT UNSIGNED NOT NULL,
  template_id     INT UNSIGNED    NOT NULL,
  slot            ENUM('ARMOR','PANTS','GLOVES','BOOTS','RING') NOT NULL,
  location        ENUM('BAG','EQUIPPED') NOT NULL DEFAULT 'BAG',   -- only BAG counts toward the 2,000 limit
  upgrade_percent DECIMAL(6,2)    NOT NULL DEFAULT 0,
  gen_key         VARCHAR(32)     NULL,
  version         INT UNSIGNED    NOT NULL DEFAULT 1,
  created_at      TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (item_id),
  KEY idx_item_char_loc (character_id, location, item_id),
  CONSTRAINT fk_item_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One row per stat line of an item (never positional JSON). At most 4 extra lines besides BASE (enforced by the server).
CREATE TABLE IF NOT EXISTS item_stat_line (
  item_id    BIGINT UNSIGNED NOT NULL,
  line_no    TINYINT UNSIGNED NOT NULL,
  stat_key   VARCHAR(40)     NOT NULL,
  `value`    DECIMAL(12,2)   NOT NULL,
  source     ENUM('BASE','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','GEN') NOT NULL,
  PRIMARY KEY (item_id, line_no),
  CONSTRAINT fk_isl_item FOREIGN KEY (item_id)  REFERENCES item_instance (item_id) ON DELETE CASCADE,
  CONSTRAINT fk_isl_stat FOREIGN KEY (stat_key) REFERENCES stat_type (stat_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Stackable materials (fragments, stones). template_id gets its foreign key to material_template in V004.
CREATE TABLE IF NOT EXISTS material_stack (
  character_id BIGINT UNSIGNED NOT NULL,
  template_id  INT UNSIGNED    NOT NULL,
  quantity     INT UNSIGNED    NOT NULL DEFAULT 0,
  PRIMARY KEY (character_id, template_id),
  CONSTRAINT fk_mat_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The 10 slots of a character: 5 gear + 1 skin + 4 permanent support items. A gear slot holds an item_instance, SKIN holds an owned skin.
CREATE TABLE IF NOT EXISTS character_equipment (
  character_id BIGINT UNSIGNED NOT NULL,
  slot         ENUM('ARMOR','PANTS','GLOVES','BOOTS','RING','SKIN','SUPPORT_1','SUPPORT_2','SUPPORT_3','SUPPORT_4') NOT NULL,
  item_id      BIGINT UNSIGNED NULL,
  costume_id   INT UNSIGNED    NULL,
  PRIMARY KEY (character_id, slot),
  UNIQUE KEY uq_equip_item (item_id),
  CONSTRAINT fk_equip_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_equip_item FOREIGN KEY (item_id) REFERENCES item_instance (item_id),
  CONSTRAINT fk_equip_skin FOREIGN KEY (character_id, costume_id) REFERENCES owned_skin (character_id, costume_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Daily purchase limit (e.g. Potara fragments). The account is the key because the earring is per account.
-- day_key is the date at 00:00 UTC+7 (CONFIRMED reset time).
CREATE TABLE IF NOT EXISTS daily_purchase_counter (
  account_id   BIGINT UNSIGNED NOT NULL,
  shop_item_id INT UNSIGNED    NOT NULL,
  day_key      DATE            NOT NULL,
  cnt          INT UNSIGNED    NOT NULL DEFAULT 0,
  PRIMARY KEY (account_id, shop_item_id, day_key),
  CONSTRAINT fk_dpc_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;

-- ---------------------------------------------------------------------------------------------------------------
-- Seed for race_default_skin: NRO avatars 412-414 (EARTH), 415-417 (NAMEC), 418-420 (XAYDA); they are the only avatars in the
-- catalog with nro_sold = 1 and no gem price, so they are the proposed starters (PROPOSED, admin edits afterwards).
-- ---------------------------------------------------------------------------------------------------------------
INSERT INTO race_default_skin (race, costume_id, is_equipped, updated_by) VALUES
  ('EARTH', 412, 1, 'seed'), ('EARTH', 413, 0, 'seed'), ('EARTH', 414, 0, 'seed'),
  ('NAMEC', 415, 1, 'seed'), ('NAMEC', 416, 0, 'seed'), ('NAMEC', 417, 0, 'seed'),
  ('XAYDA', 418, 1, 'seed'), ('XAYDA', 419, 0, 'seed'), ('XAYDA', 420, 0, 'seed');
