-- BattleBallServer: small mobs that spawn en masse are not bosses (owner decision 2026-10-07).
-- "tất cả đều là boss; quái nhỏ ra ồ ạt khi chiến đấu là nhóm Sự kiện Halloween và đám Xên con".
-- For a database that already ran the earlier V009 (the regenerated V009 already contains these values; this file is idempotent).
--   * boss_group.tag gets 'MOB' (a group of small mobs) and the Halloween group becomes MOB.
--   * dungeon_boss.role gets 'MINION' (a small mob that spawns en masse in that dungeon's fights); the seven "Xên con" of the Cell group use it.
-- The Halloween group is NOT assigned to a dungeon (OPEN: which fights spawn it).

SET NAMES utf8mb4;

ALTER TABLE boss_group MODIFY tag ENUM('DUNGEON','BOSS','MOB','SPECIAL','EVENT') NOT NULL;
ALTER TABLE dungeon_boss MODIFY role ENUM('NORMAL','MINI','FINAL','MINION') NOT NULL DEFAULT 'NORMAL';

UPDATE boss_group SET tag = 'MOB', label_vi = 'Quái nhỏ Halloween (Bí ma, Dơi, Ma trơi)', updated_by = 'owner-choice' WHERE group_key = 'event';

UPDATE dungeon_boss x
  JOIN boss_template b ON b.boss_id = x.boss_id
  SET x.role = 'MINION', x.updated_by = 'owner-choice'
  WHERE b.name_vi LIKE 'Xên con%' AND x.role = 'NORMAL';

UPDATE static_data_version SET version = version + 1, updated_by = 'owner-choice' WHERE domain = 'DUNGEON';
