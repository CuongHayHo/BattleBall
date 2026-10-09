-- BattleBallServer: training fixes asked by the owner on 2026-10-09.
--  * skills the character has not learned are no longer usable in training (unlearned_skill_level 0 = only learned skills)
--  * the punch reached too far: the hit radius of the dummies is smaller (a punch hits at its length + the radius of the target)
SET NAMES utf8mb4;

UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.unlearned_skill_level', 0, '$.dummies[0].radius', 14, '$.dummies[1].radius', 12),
       updated_by = 'V032'
 WHERE config_key = 'training';

UPDATE static_data_version SET version = version + 1, updated_by = 'V032' WHERE domain = 'BALANCE';
