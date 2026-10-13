-- BattleBallServer: every boss has a tenth of the HP of the table and hits harder (owner request 2026-10-13: "giam mau all boss di 10 lan, tang dame cua boss len").
--  * balance_config.boss_scale = {"hp": 0.1, "atk": 3}: multipliers for the bosses only (the normal, mini and final bosses and every form of the evolving boss), not for the small
--    monsters of the waves. They come on top of the difficulty and tier multipliers (dungeon_difficulty). Missing = 1.
--  * The damage x3 is PROPOSED (the owner gave no number). Simulated with the bot of the tests and the AI allies, 5 runs each, boss damage x1, x2, x3, x5, x8: a boss now lives a tenth
--    as long, so it deals much less in total and x3 changed no result (a level 1 squad with no gear on easy in dungeon 1, level 10 with the first gear tier on hard, level 45 on easy
--    in a middle dungeon, a full build on hell: all won 5/5 with 0 characters down); x5 already loses a full build on hell in a late dungeon (0/5), x8 loses a level 45 squad on
--    easy in a middle dungeon (2/5). Edit the number on the admin page.
SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('boss_scale', '{"hp":0.1,"atk":3}',
   'Multipliers of the HP and of the damage of every boss (not of the small monsters), on top of the difficulty: HP x0.1, damage x3', 'V052')
ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = 'V052';

UPDATE static_data_version SET version = version + 1, updated_by = 'V052' WHERE domain = 'BALANCE';
