-- BattleBallServer: gem (đá quý) income, GDD section 6 ("Đá quý", PROPOSED 2026-10-07). Owner: "làm" (2026-10-07).
--   * first time a character defeats the FINAL boss of a dungeon (and wins the match): 20 gems, once per character per dungeon;
--   * a timed dungeon cleared within its time limit: 10 gems, at most 5 times per character per day (day starts 00:00 UTC+7);
--   * raid boss: first kill 100 gems, repeats 10 gems up to 3 per day. No dungeon is a raid yet, so those two rules are DISABLED.
-- Gems are never bought with gold and never dropped by mobs. The server writes currency_ledger rows (reason FIRST_CLEAR / TIMED_CLEAR / RAID).
-- Idempotent schema part (guarded); the seed rows run once.

SET NAMES utf8mb4;

SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dungeon_template' AND COLUMN_NAME = 'time_limit_s');
SET @ddl = IF(@has_col = 0, 'ALTER TABLE dungeon_template ADD COLUMN time_limit_s INT UNSIGNED NULL AFTER bosses_per_match', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS gem_reward_rule (
  rule_id     INT UNSIGNED NOT NULL AUTO_INCREMENT,
  reward_type ENUM('FIRST_CLEAR','TIMED_CLEAR','RAID_FIRST','RAID_REPEAT') NOT NULL,
  dungeon_id  INT UNSIGNED NULL,                      -- NULL = every dungeon the type applies to
  gems        INT UNSIGNED NOT NULL,
  daily_cap   TINYINT UNSIGNED NULL,                  -- per character per day; NULL = no cap
  enabled     TINYINT(1)   NOT NULL DEFAULT 1,
  note        VARCHAR(255) NULL,
  updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)  NULL,
  PRIMARY KEY (rule_id),
  UNIQUE KEY uq_gem_rule (reward_type, dungeon_id),
  CONSTRAINT fk_gem_dungeon FOREIGN KEY (dungeon_id) REFERENCES dungeon_template (dungeon_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player side: which dungeons a character already cleared for the first time, and per-day reward counters.
CREATE TABLE IF NOT EXISTS character_first_clear (
  character_id BIGINT UNSIGNED NOT NULL,
  dungeon_id   INT UNSIGNED    NOT NULL,
  cleared_at   TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (character_id, dungeon_id),
  CONSTRAINT fk_cfc_char    FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_cfc_dungeon FOREIGN KEY (dungeon_id)   REFERENCES dungeon_template (dungeon_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS character_daily_counter (
  character_id BIGINT UNSIGNED NOT NULL,
  counter_key  VARCHAR(32)     NOT NULL,              -- TIMED_CLEAR, RAID_REPEAT
  day_key      DATE            NOT NULL,              -- the date at 00:00 UTC+7
  cnt          INT UNSIGNED    NOT NULL DEFAULT 0,
  PRIMARY KEY (character_id, counter_key, day_key),
  CONSTRAINT fk_cdc_char FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO gem_reward_rule (reward_type, dungeon_id, gems, daily_cap, enabled, note, updated_by) VALUES
  ('FIRST_CLEAR', NULL, 20, NULL, 1, 'Lần đầu hạ boss cuối của mỗi phó bản (thắng trận): 20 đá quý, một lần cho mỗi nhân vật', 'proposal'),
  ('TIMED_CLEAR', NULL, 10, 5, 1, 'Phó bản có giới hạn thời gian, hoàn thành trong thời gian: 10 đá quý, tối đa 5 lần mỗi ngày', 'proposal'),
  ('RAID_FIRST', NULL, 100, NULL, 0, 'Raid boss lần hạ đầu tiên (đang tắt: chưa có phó bản raid)', 'proposal'),
  ('RAID_REPEAT', NULL, 10, 3, 0, 'Raid boss các lần sau, tối đa 3 lần mỗi ngày (đang tắt: chưa có phó bản raid)', 'proposal');

-- Dungeon 2 "Thi đấu" is the timed dungeon: 4 normal bosses (45 s) + the final boss (150 s) is about 330 s of fighting, limit 480 s (8 minutes).
UPDATE dungeon_template SET time_limit_s = 480, updated_by = 'proposal' WHERE dungeon_id = 2;

INSERT INTO static_data_version (domain, version, updated_by) VALUES ('GEM', 1, 'proposal')
  ON DUPLICATE KEY UPDATE version = version + 1, updated_by = 'proposal';
UPDATE static_data_version SET version = version + 1, updated_by = 'proposal' WHERE domain = 'DUNGEON';
