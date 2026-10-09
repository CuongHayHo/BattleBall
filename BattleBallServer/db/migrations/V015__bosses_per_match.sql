-- BattleBallServer: how many real bosses one match of a dungeon contains (owner: "tự đề xuất", 2026-10-07). PROPOSED.
-- Rule used by the match server: the match always includes the dungeon's FINAL boss (and the MINI boss if the dungeon has one), the rest are
-- drawn at random without repetition from the NORMAL bosses of the dungeon until bosses_per_match is reached; a dungeon with fewer real bosses
-- uses all of them. Small mobs (role MINION) are not counted. Idempotent.

SET NAMES utf8mb4;

SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dungeon_template' AND COLUMN_NAME = 'bosses_per_match');
SET @ddl = IF(@has_col = 0,
  'ALTER TABLE dungeon_template ADD COLUMN bosses_per_match TINYINT UNSIGNED NOT NULL DEFAULT 5 AFTER tier',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE static_data_version SET version = version + 1, updated_by = 'proposal' WHERE domain = 'DUNGEON';
