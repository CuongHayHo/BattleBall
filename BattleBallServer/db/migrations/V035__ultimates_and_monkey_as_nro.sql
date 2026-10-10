-- BattleBallServer: the ultimates, Biến hình and Ma phong ba as the original game does them (owner request 2026-10-10: "đối chiếu code game gốc để tái tạo").
-- Source (CONFIRMED, read): Teamobi2026 NewSkill.java, SkillService.updateSkillSpecial, EffectSkillService (startUseSkillMonkey, setIsMonkey, finishUseMafuba, setIsBinh),
-- EffectSkin.updateMaPhongBa, NPoint (monkey: max HP + (level + 3) x 10 %, attack + (level + 3) %, critical rate 110).
SET NAMES utf8mb4;

-- Super Kamejoko (24) and Cađíc liên hoàn chưởng (25): after the gong of 2 s the beam hits everything in its rectangle, the target in focus and what lies behind it,
-- every 250 ms for 2 s (Nro: a timer of 250 ms, TIME_GONG = 2000). The owner asked for a reach of 2 chunks of the village map (chunk.json: segmentWidth 1680 Nro px).
UPDATE skill_level
   SET length_px = 3360,
       params = JSON_SET(COALESCE(params, '{}'), '$.activeMs', 2000, '$.tickMs', 250),
       updated_by = 'V035'
 WHERE skill_id IN (24, 25);

-- Ma phong ba (26): gong 2 s, everything within 500 px is sucked into the jar; 4 s later each target is a jar for 11 s (TIME_END_26 = 11000) and takes the caster's
-- max HP x level % every 500 ms, never to death. The old "trapMs" (a plain hold of 4 s without damage) is replaced.
UPDATE skill_level
   SET params = JSON_SET(JSON_REMOVE(COALESCE(params, '{}'), '$.trapMs'), '$.activeMs', 2000, '$.gatherMs', 4000, '$.jarMs', 11000, '$.tickMs', 500,
                         '$.hpPctPerLevel', 1, '$.jarDistancePx', 75),
       updated_by = 'V035'
 WHERE skill_id = 26;

-- Biến hình (13): a hold of 1.5 s (startUseSkillMonkey: timeUseSkillMonkey = 1500) before the change; the critical rate becomes 110 while it lasts (NPoint).
-- hpBonusPct = (level + 3) x 10 and dameBonusRaw = level + 3 (attack + that many %) were already in the data.
UPDATE skill_level
   SET params = JSON_SET(COALESCE(params, '{}'), '$.castMs', 1500, '$.critRatePct', 110),
       updated_by = 'V035'
 WHERE skill_id = 13;

UPDATE static_data_version SET version = version + 1, updated_by = 'V035' WHERE domain = 'SKILL';
