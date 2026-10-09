-- BattleBallServer: match records (a "run" = one match of a dungeon), item log and anomaly log. PROPOSED, owner: "tự đề xuất" (2026-10-07).
-- Needed by the server for: paying rewards once per match, first-clear / daily gem limits, the 2-revive limit, EXP per character, and the
-- "log only, review by hand" anti-cheat decision (docs/database/BattleBall.md 3.10 and section 6).
-- Depends on V003 (account, game_character), V009 (dungeon_template, boss_template).

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE IF NOT EXISTS match_run (
  run_id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  account_id        BIGINT UNSIGNED NOT NULL,
  dungeon_id        INT UNSIGNED    NOT NULL,
  controlled_char_id BIGINT UNSIGNED NOT NULL,              -- the character the player controls (the others are AI)
  fusion_partner_id BIGINT UNSIGNED NULL,                   -- Potara fusion: the secondary character, NULL when not fused
  session_token_hash CHAR(64)       NOT NULL,               -- SHA-256 of the one-time run token; makes "finish match" idempotent
  status            ENUM('ACTIVE','WON','LOST','ABANDONED','EXPIRED') NOT NULL DEFAULT 'ACTIVE',
  started_at        TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ended_at          TIMESTAMP       NULL,
  duration_s        INT UNSIGNED    NULL,
  revives_used      TINYINT UNSIGNED NOT NULL DEFAULT 0,    -- at most 2 per match (CONFIRMED rule)
  gold_earned       BIGINT UNSIGNED NOT NULL DEFAULT 0,
  gems_earned       INT UNSIGNED    NOT NULL DEFAULT 0,
  reward_applied    TINYINT(1)      NOT NULL DEFAULT 0,     -- set in the same transaction that credits wallet, EXP and drops
  PRIMARY KEY (run_id),
  UNIQUE KEY uq_run_token (session_token_hash),
  KEY idx_run_account (account_id, started_at),
  KEY idx_run_dungeon (dungeon_id, status),
  CONSTRAINT fk_run_account  FOREIGN KEY (account_id)         REFERENCES account (account_id) ON DELETE CASCADE,
  CONSTRAINT fk_run_dungeon  FOREIGN KEY (dungeon_id)         REFERENCES dungeon_template (dungeon_id),
  CONSTRAINT fk_run_ctrl     FOREIGN KEY (controlled_char_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_run_fusion   FOREIGN KEY (fusion_partner_id)  REFERENCES game_character (character_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The bosses drawn for this match (V015 rule) and when each was defeated.
CREATE TABLE IF NOT EXISTS match_run_boss (
  run_id      BIGINT UNSIGNED NOT NULL,
  order_no    TINYINT UNSIGNED NOT NULL,
  boss_id     INT UNSIGNED    NOT NULL,
  role        ENUM('NORMAL','MINI','FINAL') NOT NULL,
  defeated_at TIMESTAMP       NULL,
  PRIMARY KEY (run_id, order_no),
  CONSTRAINT fk_rboss_run  FOREIGN KEY (run_id)  REFERENCES match_run (run_id) ON DELETE CASCADE,
  CONSTRAINT fk_rboss_boss FOREIGN KEY (boss_id) REFERENCES boss_template (boss_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- EXP per character (the three characters of the squad each have their own EXP).
CREATE TABLE IF NOT EXISTS match_run_character (
  run_id       BIGINT UNSIGNED NOT NULL,
  character_id BIGINT UNSIGNED NOT NULL,
  exp_gained   BIGINT UNSIGNED NOT NULL DEFAULT 0,
  level_before TINYINT UNSIGNED NOT NULL,
  level_after  TINYINT UNSIGNED NULL,
  PRIMARY KEY (run_id, character_id),
  CONSTRAINT fk_rchar_run  FOREIGN KEY (run_id)       REFERENCES match_run (run_id) ON DELETE CASCADE,
  CONSTRAINT fk_rchar_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Every change of items and materials (drop, craft, upgrade, sell, destroy...), like history_transaction of the reference but per action.
CREATE TABLE IF NOT EXISTS item_log (
  log_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  character_id BIGINT UNSIGNED NOT NULL,
  action       ENUM('DROP','BUY','ASSEMBLE','CRAFT','UPGRADE','SELL','DESTROY','REWARD','LOST_BAG_FULL') NOT NULL,
  template_kind ENUM('ITEM','MATERIAL','SUPPORT','SKIN') NOT NULL,
  template_id  INT UNSIGNED    NOT NULL,
  item_id      BIGINT UNSIGNED NULL,                         -- item_instance when the item is a single piece
  quantity     INT             NOT NULL DEFAULT 1,           -- negative = removed
  run_id       BIGINT UNSIGNED NULL,
  created_at   TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (log_id),
  KEY idx_ilog_char (character_id, created_at),
  KEY idx_ilog_run (run_id),
  CONSTRAINT fk_ilog_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_ilog_run  FOREIGN KEY (run_id)       REFERENCES match_run (run_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Suspicious events (match too short, bosses defeated too fast, reward per hour over the limit, damage over the limit). Log only, never blocks.
CREATE TABLE IF NOT EXISTS anomaly_log (
  anomaly_id  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  account_id  BIGINT UNSIGNED NOT NULL,
  run_id      BIGINT UNSIGNED NULL,
  kind        VARCHAR(40)     NOT NULL,                      -- SHORT_MATCH, FAST_BOSS, REWARD_RATE, DAMAGE_OVER_CAP, ...
  detail      JSON            NULL,
  reviewed    TINYINT(1)      NOT NULL DEFAULT 0,
  created_at  TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (anomaly_id),
  KEY idx_anom_account (account_id, created_at),
  KEY idx_anom_open (reviewed, created_at),
  CONSTRAINT fk_anom_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE,
  CONSTRAINT fk_anom_run     FOREIGN KEY (run_id)     REFERENCES match_run (run_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;

INSERT INTO static_data_version (domain, version, updated_by) VALUES ('RUN', 1, 'proposal')
  ON DUPLICATE KEY UPDATE version = version + 1, updated_by = 'proposal';
