-- BattleBallServer: the dungeon as the owner described it on 2026-10-11 (docs/architecture/BattleBallServer.md 15.25).
--  * match_rules: a flying arena, waves of small monsters before every boss (5 waves, rushing in from outside the screen), what the small monsters do (a punch and a
--    chuong of level 1), what a boss summons, the view of the player (the AI fight only what is on the screen), the skill 2 that flies to the edge of the screen and
--    the 4 second spin of the ultimates against small monsters
--  * minion_cap_per_match: 20 -> 550 (5 stages x 90 monsters of the waves + what the bosses summon)
--  * dungeon_boss: the Halloween group (Bi ma, Doi, Ma troi) is the small monster of every dungeon that has none of its own (Cell keeps its Xen con)
--  * boss_actions gets "bomb" (the Qua cau kenh khi of an Earth boss); boss_planet_kits: every boss draws a planet at random for the match and fights with the
--    skills of that planet
SET NAMES utf8mb4;

UPDATE balance_config
   SET value_json = JSON_SET(value_json,
        '$.dg_arena_w', 1800, '$.dg_arena_h', 640,
        '$.dg_view_w', 640, '$.dg_view_h', 360, '$.dg_view_min_w', 240, '$.dg_view_min_h', 160, '$.dg_view_max_w', 1400, '$.dg_view_max_h', 900,
        '$.dg_waves', JSON_ARRAY(12, 15, 18, 20, 25),
        '$.dg_first_wave_s', 2.0, '$.dg_wave_next_alive_le', 5, '$.dg_wave_next_after_s', 10, '$.dg_alive_cap', 30, '$.dg_wave_spawn_s', 1.5,
        '$.dg_shooter_cap', 6, '$.dg_ai_defend_px', 220,
        '$.dg_minion_speed', 220, '$.dg_minion_shoot_range', 260, '$.dg_minion_melee_range', 40, '$.dg_minion_punch_s', 1.2,
        '$.dg_minion_shoot_min_s', 2.5, '$.dg_minion_shoot_max_s', 3.5, '$.dg_minion_cast_s', 0.5,
        '$.dg_minion_punch_mult', 1.0, '$.dg_minion_shoot_mult', 1.5, '$.dg_minion_dmg_mult', 0.06, '$.dg_minion_shot_radius', 24,
        '$.dg_summon_first', 5, '$.dg_summon_batch', 3, '$.dg_summon_every_s', 8, '$.dg_summon_alive_cap', 8, '$.dg_summon_cap_per_boss', 20,
        '$.dg_ult_spin_s', 4.0, '$.dg_ult_spin_length', 800, '$.dg_skill2_to_view_edge', 1,
        '$.dg_bomb_radius', 380, '$.dg_bomb_telegraph_s', 2.5),
       updated_by = 'V040'
 WHERE config_key = 'match_rules';

UPDATE balance_config SET value_json = '550', updated_by = 'V040' WHERE config_key = 'minion_cap_per_match';

UPDATE balance_config
   SET value_json = '[{"name":"ultimate","cooldown_s":60,"damage_mult":3.0,"avoidable":true},{"name":"shield","cooldown_s":45,"damage_mult":0.0,"avoidable":null},{"name":"stun","cooldown_s":25,"damage_mult":0.5,"avoidable":true},{"name":"bomb","cooldown_s":20,"damage_mult":2.0,"avoidable":true},{"name":"focus","cooldown_s":15,"damage_mult":1.0,"avoidable":false},{"name":"beam","cooldown_s":6,"damage_mult":1.5,"avoidable":true},{"name":"punch","cooldown_s":0,"damage_mult":1.0,"avoidable":true}]',
       updated_by = 'V040'
 WHERE config_key = 'boss_actions';

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('boss_planet_kits',
   '{"EARTH":{"punch":0,"beam":1,"ultimate":24,"stun":6,"bomb":10,"focus":0,"actions":["ultimate","shield","stun","bomb","focus","beam","punch"]},"NAMEC":{"punch":2,"beam":3,"ultimate":3,"focus":11,"actions":["ultimate","shield","focus","beam","punch"]},"XAYDA":{"punch":4,"beam":5,"ultimate":25,"focus":4,"actions":["ultimate","shield","focus","beam","punch"]}}',
   'Skills of a boss by the planet it draws at random for a match: the skill ids of its punch, chuong (beam), ultimate, stun (Earth only), bomb (Earth only) and focus, and which actions of boss_actions it has', 'V040')
ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), updated_by = 'V040';

-- the small monsters of the dungeons that have none (every dungeon except the Cell one): the three Halloween monsters, ordinary (not elite) monsters
INSERT IGNORE INTO dungeon_boss (dungeon_id, boss_id, role, mob_tier, sort_order, updated_by)
SELECT d.dungeon_id, b.boss_id, 'MINION', 'MOB', 900 + b.boss_id, 'V040'
  FROM dungeon_template d
  JOIN boss_template b ON b.group_key = 'event'
 WHERE NOT EXISTS (SELECT 1 FROM dungeon_boss x WHERE x.dungeon_id = d.dungeon_id AND x.role = 'MINION');

UPDATE static_data_version SET version = version + 1, updated_by = 'V040' WHERE domain IN ('BALANCE', 'DUNGEON');
