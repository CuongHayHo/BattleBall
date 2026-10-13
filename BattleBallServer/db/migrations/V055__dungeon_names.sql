-- BattleBallServer: the dungeons are named after what is in them (docs/architecture/BattleBallServer.md 15.38, owner request 2026-10-13: "the dungeons have no name that says what
-- drops and which boss and monsters appear"). The list of Cui now also shows, for each dungeon, its bosses, its small monsters and what can drop (DungeonSummary, built from the
-- boss list and the drop rules, so it always matches the data); the name is the short title:
--   1 Thần Điện (Karin ... Yajirô)   2 Đại hội võ thuật (Yamcha, with a time limit)   3 Khỉ đột 7 dạng (the evolving ape, book materials x3)   4 Cađích và Nađíc
--   5 Dr Lychee (Hatchiyack)   6 Xên và các Xên con   7 Cumber   8 Fide đại ca   9 Fide Vàng   10 Bui Bui đến Ya côn   11 Mabư đến Kid Bư (earring shards)   12 Kuku và Rambo
--   13 Tiểu đội Số 1 đến 4
-- The names of seven bosses had a note of the source data in them ("(tên trong code: trống)"), which the player saw over the monster; they are the plain names now.
SET NAMES utf8mb4;

UPDATE dungeon_template SET name_vi = CASE dungeon_id
    WHEN 1 THEN 'Thần Điện – Yajirô'
    WHEN 2 THEN 'Đại hội võ thuật'
    WHEN 3 THEN 'Khỉ đột 7 dạng – Sách'
    WHEN 4 THEN 'Cađích – Nađíc'
    WHEN 5 THEN 'Dr Lychee – Hatchiyack'
    WHEN 6 THEN 'Xên – Xên con'
    WHEN 7 THEN 'Cumber – Super Cumber'
    WHEN 8 THEN 'Fide đại ca 1–3'
    WHEN 9 THEN 'Fide Vàng'
    WHEN 10 THEN 'Bui Bui – Ya côn'
    WHEN 11 THEN 'Mabư – Kid Bư (Bông tai)'
    WHEN 12 THEN 'Kuku – Rambo'
    WHEN 13 THEN 'Tiểu đội Số 1–4'
    ELSE name_vi END,
  updated_by = 'V055'
 WHERE dungeon_id BETWEEN 1 AND 13;

UPDATE boss_template SET name_vi = CASE boss_id
    WHEN 4 THEN 'Saibamen'
    WHEN 11 THEN 'Hatchiyack'
    WHEN 58 THEN 'Death Beam'
    ELSE TRIM(SUBSTRING_INDEX(name_vi, ' (tên trong code', 1)) END,
  updated_by = 'V055'
 WHERE name_vi LIKE '%(tên trong code%';

UPDATE static_data_version SET version = version + 1, updated_by = 'V055' WHERE domain IN ('DUNGEON', 'MONSTER');
