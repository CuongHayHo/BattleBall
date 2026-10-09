-- BattleBallServer: tier of a group of small mobs (owner decision 2026-10-07: the Halloween group is "mob thường" = normal mob).
-- boss_group.mob_tier is only for groups with tag 'MOB'; a dungeon_boss row of role MINION keeps its own mob_tier (V011).
-- Idempotent.

SET NAMES utf8mb4;

SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'boss_group' AND COLUMN_NAME = 'mob_tier');
SET @ddl = IF(@has_col = 0,
  'ALTER TABLE boss_group ADD COLUMN mob_tier ENUM(''MOB'',''ELITE'') NULL AFTER tag',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE boss_group SET mob_tier = 'MOB', updated_by = 'owner-choice' WHERE group_key = 'event' AND tag = 'MOB';

UPDATE static_data_version SET version = version + 1, updated_by = 'owner-choice' WHERE domain = 'DUNGEON';
