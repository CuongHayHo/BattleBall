-- BattleBallServer: a MINION (small mob that spawns en masse) is either a normal mob or an elite mob (GDD section 7.1 tiers).
-- Owner decision 2026-10-07: "Xên con" are Mob elite. The Halloween group stays unclassified (NULL = OPEN).
-- Idempotent: safe to run on a database that already has the column.

SET NAMES utf8mb4;

SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dungeon_boss' AND COLUMN_NAME = 'mob_tier');
SET @ddl = IF(@has_col = 0,
  'ALTER TABLE dungeon_boss ADD COLUMN mob_tier ENUM(''MOB'',''ELITE'') NULL AFTER role',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE dungeon_boss x
  JOIN boss_template b ON b.boss_id = x.boss_id
  SET x.mob_tier = 'ELITE', x.updated_by = 'owner-choice'
  WHERE x.role = 'MINION' AND b.name_vi LIKE 'Xên con%';

UPDATE static_data_version SET version = version + 1, updated_by = 'owner-choice' WHERE domain = 'DUNGEON';
