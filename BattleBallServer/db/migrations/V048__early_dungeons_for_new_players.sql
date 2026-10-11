-- BattleBallServer: the basic (early tier) dungeons are easier for a new player (docs/architecture/BattleBallServer.md 15.35, owner report 2026-10-12: "the bosses of the basic
-- dungeons have a lot of HP and hit hard, a new player cannot play").
--  * dungeon_difficulty.levels[].tier_mult: an extra multiplier of the HP and of the damage of the monsters for the dungeons of a tier ({"early": {"hp": x, "atk": y}}), on top of
--    the multipliers of the difficulty. Missing = 1. Only the early tier is lowered: mid and late are unchanged.
--  * Why: a new account has level 1 characters, no gear (no gold either), skills at level 1. Simulated (the bot of the tests plus the AI of the allies, 6 runs each) on dungeon 1: with
--    the numbers of V046 (easy: HP x0,6, damage x0,2) that squad lost every run before it killed one boss; with HP x0,3 and damage x0,04 it wins 6/6 (0 to 5 characters down).
--    A squad of level 10 with the first gear tier wins 6/6 at damage x0,1 and loses at x0,15, which is where hard lands (x0,15).
--      easy: HP 0,6 x 0,5 = 0,3 and damage 0,2 x 0,2 = 0,04;  hard: 0,9 x 0,6 = 0,54 and 0,33 x 0,45 = 0,15;  hell: 1,3 x 0,7 = 0,91 and 0,55 x 0,6 = 0,33.
SET NAMES utf8mb4;

UPDATE balance_config
   SET value_json = JSON_SET(value_json,
         '$.levels[0].tier_mult', JSON_OBJECT('early', JSON_OBJECT('hp', 0.5, 'atk', 0.2)),
         '$.levels[1].tier_mult', JSON_OBJECT('early', JSON_OBJECT('hp', 0.6, 'atk', 0.45)),
         '$.levels[2].tier_mult', JSON_OBJECT('early', JSON_OBJECT('hp', 0.7, 'atk', 0.6))),
       updated_by = 'V048'
 WHERE config_key = 'dungeon_difficulty'
   AND JSON_EXTRACT(value_json, '$.levels[0].key') = 'EASY' AND JSON_EXTRACT(value_json, '$.levels[2].key') = 'HELL';

UPDATE static_data_version SET version = version + 1, updated_by = 'V048' WHERE domain = 'BALANCE';
