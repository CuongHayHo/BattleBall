-- BattleBallServer: the rules of the stat stone and the crystal stone (the "Ep sao" and "Pha le hoa" of Ba Hat Mit), and where GEN stones and the fragments of the
-- Huy Diet and Thien Su sets drop (docs/architecture/BattleBallServer.md 15.27). Owner request 2026-10-11 ("bo sung theo ke hoach"): the plan of 15.19 had no rules for these.
--
-- The numbers of the stat stone and of the crystal stone are the GDD (section 6) as it stands: a stat line is a % of the TOTAL stat of the character (not of the piece), at most 25%
-- (5% for the critical rate), +1.5 to 2% a success (+0.1 to 0.5% for the critical rate), the chance of a success falls as the line nears its maximum, no two lines of the same
-- type on a piece, a stone may also change the type of a line at random; the crystal (red) line has a maximum of 50%, starts at 10%, may repeat the type of another line, and
-- is raised with stat stones. A piece has at most 4 extra lines: 3 stat lines and the red one (GDD: "toi da 50% trong 3 chi so con lai cua do"). What the GDD leaves open is
-- PROPOSED here and editable on the admin page: the chances by how full a line is, the cost of one action (one stone), the steps of the red line (2 to 3%), and that the red
-- line never takes the critical rate (a 50% critical rate would break the game).
-- Drops (PROPOSED, all editable on the admin page "Bang roi do"): see the comments at the inserts.

SET NAMES utf8mb4;

ALTER TABLE item_log MODIFY action ENUM('DROP','BUY','ASSEMBLE','CRAFT','UPGRADE','SELL','DESTROY','REWARD','LOST_BAG_FULL','GEN_REROLL','GEN_SLOT',
                                        'STAT_ADD','STAT_RAISE','STAT_RETYPE','CRYSTAL') NOT NULL;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('stat_stone_rules',
   '{"max_lines":3,
     "types":[
       {"key":"ATK_PCT","cap":25,"step_min":1.5,"step_max":2.0},
       {"key":"HP_PCT","cap":25,"step_min":1.5,"step_max":2.0},
       {"key":"ARMOR_PCT","cap":25,"step_min":1.5,"step_max":2.0},
       {"key":"KI_PCT","cap":25,"step_min":1.5,"step_max":2.0},
       {"key":"CRIT_RATE_PCT","cap":5,"step_min":0.1,"step_max":0.5},
       {"key":"CRIT_DMG_PCT","cap":25,"step_min":1.5,"step_max":2.0}],
     "success":[[0.5,100],[0.7,80],[0.85,60],[0.95,40],[1.01,20]],
     "add_stones":1,"raise_stones":1,"retype_stones":1,
     "crystal":{"types":["ATK_PCT","HP_PCT","ARMOR_PCT","KI_PCT","CRIT_DMG_PCT"],"start":10,"cap":50,"step_min":2.0,"step_max":3.0,"stones":1}}',
   'Stat stone (Ep sao) and crystal stone (Pha le hoa): lines of a piece. types: allowed stats with their maximum and the step of one success; success: [share of the maximum already reached below which, chance in %]; crystal: the red line', 'V041')
ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), updated_by = 'V041';

-- ----------------------------------------------------------------------------------------------------------------------------------------------------------------------
-- Drops of the Huy Diet set (#106 to #110, wearable from level 30, x1.5) and the Thien Su set (#111 to #115, from level 60, x2). The Than Linh fragments (#101 to #105) drop from
-- every boss at 1.0 / 1.2 / 1.4 % (early / mid / late dungeon), mini-boss 1.5 %, final boss 3 % (1 to 2). The Huy Diet fragments start in the middle dungeons (3, 4, 10, 13) and
-- are commoner in the late ones (5, 7, 8, 9, 11); the Thien Su fragments are for the late dungeons only and are the rarest: expect about 100 matches for ten fragments of one kind of
-- the Than Linh set, about 125 for the Huy Diet set and about 235 for the Thien Su set (a late dungeon, five kinds of fragment each at the same rate).
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by)
SELECT c.source_class, c.tier, m.template_id, c.rate_pct, c.qty_min, c.qty_max, 'Manh Huy Diet (moi loai cung ti le)', 'V041'
  FROM material_template m
  JOIN (SELECT 'BOSS' source_class, 'mid' tier, 0.80 rate_pct, 1 qty_min, 1 qty_max UNION ALL SELECT 'BOSS', 'late', 1.10, 1, 1
        UNION ALL SELECT 'MINI', 'mid', 1.00, 1, 1 UNION ALL SELECT 'MINI', 'late', 1.20, 1, 1
        UNION ALL SELECT 'FINAL', 'mid', 2.00, 1, 2 UNION ALL SELECT 'FINAL', 'late', 2.50, 1, 2) c
 WHERE m.template_id BETWEEN 106 AND 110;

INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by)
SELECT c.source_class, c.tier, m.template_id, c.rate_pct, c.qty_min, c.qty_max, 'Manh Thien Su (moi loai cung ti le)', 'V041'
  FROM material_template m
  JOIN (SELECT 'BOSS' source_class, 'late' tier, 0.50 rate_pct, 1 qty_min, 1 qty_max
        UNION ALL SELECT 'MINI', 'late', 0.70, 1, 1 UNION ALL SELECT 'FINAL', 'late', 1.50, 1, 2) c
 WHERE m.template_id BETWEEN 111 AND 115;

-- GEN stone (#204: changes a GEN line): about as common as the stat stone, a little less. GEN slot stone (#205, six wins of gold in the shop): only the final boss, rare, like the
-- crystal stone but commoner.
INSERT INTO drop_rule (source_class, tier, material_template_id, rate_pct, qty_min, qty_max, note, updated_by) VALUES
  ('ELITE', 'ANY',   204, 0.300, 1, 1, 'Da GEN', 'V041'),
  ('BOSS',  'early', 204, 0.500, 1, 1, 'Da GEN', 'V041'),
  ('BOSS',  'mid',   204, 0.800, 1, 1, 'Da GEN', 'V041'),
  ('BOSS',  'late',  204, 1.100, 1, 1, 'Da GEN', 'V041'),
  ('MINI',  'ANY',   204, 1.500, 1, 1, 'Da GEN', 'V041'),
  ('FINAL', 'ANY',   204, 3.000, 1, 2, 'Da GEN', 'V041'),
  ('FINAL', 'early', 205, 0.200, 1, 1, 'Da mo o GEN', 'V041'),
  ('FINAL', 'mid',   205, 0.500, 1, 1, 'Da mo o GEN', 'V041'),
  ('FINAL', 'late',  205, 1.000, 1, 1, 'Da mo o GEN', 'V041');
