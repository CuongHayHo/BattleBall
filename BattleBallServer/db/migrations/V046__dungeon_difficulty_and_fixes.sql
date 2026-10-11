-- BattleBallServer: three difficulties for every dungeon, the Namek revive reach, and the damage of Qua cau kenh khi
-- (docs/architecture/BattleBallServer.md 15.33, owner request 2026-10-11).
--  * dungeon_difficulty (balance_config): Easy / Hard / Hell. Multipliers over the monster table (monster_stat_class x boss_template.hp_mult/atk_mult): the HP and the damage of every
--    monster and boss, and what the run pays (gold and EXP, chance of every drop). The default is the easiest, for a new player. PROPOSED numbers, found by simulating a squad of
--    three at the recommended level of each tier with the first gear tier (easy: it wins, with few deaths; hard: it needs upgraded gear; hell: it needs a full build).
--    The table itself (monster_stat_class) is unchanged: it was designed for a full build and kills a new squad in a minute (a squad of three with the first gear tier won 0 of 6
--    runs at the old numbers), which is what the owner reported ("the damage of the monsters and bosses is too big for a new player").
--  * match_run.difficulty: the difficulty a run was started at.
--  * match_rules.revive_skill_reach_px: how far from the healer a dead ally can be and still be revived by Tri thuong (the body lies where it fell; before, the same 170 px as a heal).
--  * skill 10 (Qua cau kenh khi): the damage is atkMult x attack (the NRO damage of the level / 100, 5 to 11) plus 10% of the HP of the victim, but that part is capped at
--    hpTermCapAtkMult x attack. Before it was 10 x attack plus the whole 10% of the HP, the same at every level: against a boss of 13 million HP one cast took 6% of its life.
SET NAMES utf8mb4;

SET @has_col = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'match_run' AND COLUMN_NAME = 'difficulty');
SET @ddl = IF(@has_col = 0, 'ALTER TABLE match_run ADD COLUMN difficulty VARCHAR(8) NOT NULL DEFAULT ''EASY''', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('dungeon_difficulty',
   '{"default":"EASY","levels":[
      {"key":"EASY","name":"Dễ","hp_mult":0.6,"atk_mult":0.2,"reward_mult":1.0,"drop_mult":1.0,"note":"Cho người mới: quái và boss yếu, đánh nhanh hơn."},
      {"key":"HARD","name":"Khó","hp_mult":0.9,"atk_mult":0.33,"reward_mult":1.6,"drop_mult":1.3,"note":"Cần đồ đã nâng cấp. Thưởng vàng, EXP x1,6, rơi đồ x1,3."},
      {"key":"HELL","name":"Địa ngục","hp_mult":1.3,"atk_mult":0.55,"reward_mult":2.5,"drop_mult":1.7,"note":"Cần đội full đồ và đá. Thưởng vàng, EXP x2,5, rơi đồ x1,7."}]}',
   'Difficulties of a dungeon run: multipliers of the HP and damage of the monsters and of the gold, EXP and drop chance of the reward; the first level that is named by default is chosen when the client sends none',
   'V046')
ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = 'V046';

UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.revive_skill_reach_px', 320),
       updated_by = 'V046'
 WHERE config_key = 'match_rules';

UPDATE skill_level
   SET params = JSON_SET(JSON_REMOVE(params, '$.damageFormula'),
         '$.atkMult', damage_raw / 100.0, '$.hpPct', 10, '$.hpTermCapAtkMult', 6,
         '$.damageFormula', 'atkMult x attack + hpPct% of the HP of each victim (at most hpTermCapAtkMult x attack)'),
       updated_by = 'V046'
 WHERE skill_id = 10;

UPDATE static_data_version SET version = version + 1, updated_by = 'V046' WHERE domain IN ('BALANCE', 'SKILL');
