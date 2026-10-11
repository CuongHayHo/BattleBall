-- BattleBallServer: the evolving ape boss of the Hoa Khi Xayda dungeon (#3), and the rule that no boss has the self-destruct of the Xayda
-- (docs/architecture/BattleBallServer.md 15.30, owner request 2026-10-11).
--  * boss_evolutions: dungeon 3 is ONE boss that goes through the seven "Khi dot" forms (boss templates 78 to 84, level 1 to 7). When the HP of a form is nearly gone
--    (hp_trigger_pct) it charges like the self-destruct for charge_s seconds (it cannot be hurt and hurts nobody), then turns into the next form with all its HP back.
--    Every form counts as a boss killed (gold, EXP, drops), the seventh as the final boss. The waves of small monsters come once, before the first form.
--  * boss_planet_kits.APE: it fights with every skill of the Xayda that a boss can use: Galick (punch, #4), Antomic (beam, #5), Trói (bind, #23), Huýt sáo (whistle, #21),
--    Tái tạo năng lượng (regen, #8), Cađíc liên hoàn chưởng (ultimate, #25); Biến hình (#13) is the evolution itself; the Tự phát nổ (#14) is NOT used. The cooldown of a move
--    is the cooldown of its skill at level 7 divided by cd_divisor when it is at least cd_long_s seconds (Cađíc 110 s -> 11 s, Trói 45 -> 4.5, Huýt sáo 180 -> 18, Tái tạo 25 -> 2.5).
--  * boss_banned_skills: no boss uses these skills, whatever kit it has (14 = Tự phát nổ). The engine skips a move whose skill is banned.
--  * boss_actions: the moves bind, whistle and regen (their cooldown comes from the skill, damage_mult 0: they hurt nobody).
SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('boss_evolutions',
   '{"3":{"boss_ids":[78,79,80,81,82,83,84],"hp_trigger_pct":10,"charge_s":3.0,"kit":"APE"}}',
   'A dungeon whose boss evolves: the boss templates of its forms in order, the share of HP left that starts the evolution, the seconds of charge (it cannot be hurt) and the kit of moves', 'V043'),
  ('boss_banned_skills', '[14]', 'Skills no boss may use (14 = Tu phat no of the Xayda)', 'V043')
ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), updated_by = 'V043';

UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.APE', JSON_OBJECT(
         'punch', 4, 'beam', 5, 'ultimate', 25,
         'actions', JSON_ARRAY('ultimate', 'bind', 'whistle', 'regen', 'beam', 'punch'),
         'skill_level', 7, 'cd_divisor', 10, 'cd_long_s', 10,
         'cd_skills', JSON_OBJECT('ultimate', 25, 'bind', 23, 'whistle', 21, 'regen', 8, 'beam', 5, 'punch', 4),
         'bind_s', 3, 'regen_s', 3, 'regen_pct_per_s', 3, 'whistle_s', 8, 'whistle_mult', 1.5)),
       updated_by = 'V043'
 WHERE config_key = 'boss_planet_kits';

UPDATE balance_config
   SET value_json = JSON_ARRAY_APPEND(JSON_ARRAY_APPEND(JSON_ARRAY_APPEND(value_json,
         '$', JSON_OBJECT('name', 'bind', 'cooldown_s', 45, 'damage_mult', 0.0, 'avoidable', NULL)),
         '$', JSON_OBJECT('name', 'whistle', 'cooldown_s', 180, 'damage_mult', 0.0, 'avoidable', NULL)),
         '$', JSON_OBJECT('name', 'regen', 'cooldown_s', 25, 'damage_mult', 0.0, 'avoidable', NULL)),
       updated_by = 'V043'
 WHERE config_key = 'boss_actions' AND JSON_SEARCH(value_json, 'one', 'bind', NULL, '$[*].name') IS NULL;
