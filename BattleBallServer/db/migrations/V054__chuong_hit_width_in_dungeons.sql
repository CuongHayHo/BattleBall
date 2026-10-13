-- BattleBallServer: in a dungeon a chuong (Kamejoko, Masenko, Antomic) hits what its dart touches (docs/architecture/BattleBallServer.md 15.37, owner report 2026-10-13: "skill2 kills
-- every monster when its damage is big, even the ones the animation does not touch").
--  * match_rules.dg_chuong_radius_px: the radius of the hit band of the chuong in a dungeon, in Nro px. The data gives the skill a width of 160 to 220 px (a band of 80 to 110 px on each
--    side of the line) while the dart that is drawn is a few dozen px wide, so monsters up to 110 px from the dart were hit and died. The engine now takes the smaller of the data and
--    this number (a monster counts as touched when its centre is within this radius plus its own radius of the line: 28 + 18 for a small one). Training in the village is unchanged.
SET NAMES utf8mb4;

UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.dg_chuong_radius_px', 28),
       updated_by = 'V054'
 WHERE config_key = 'match_rules';

UPDATE static_data_version SET version = version + 1, updated_by = 'V054' WHERE domain = 'BALANCE';
