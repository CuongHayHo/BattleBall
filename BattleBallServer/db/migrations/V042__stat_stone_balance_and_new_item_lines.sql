-- BattleBallServer: balance of the stat stone and the crystal stone, and the lines a new piece starts with (docs/architecture/BattleBallServer.md 15.28).
-- Owner request 2026-10-11: "can bang: so da can va ti le roi; mon moi ghep hoac mua khong co san dong hay GEN; chua co xem chi so truoc va sau khi ep".
--  * success: a gentler curve. Simulated (SourceData/tools/balance_stat_stones.py): the average number of stat stones to bring a stat line from its first step to the maximum
--    falls from 21 to 17 (the red line from 27 to 21, the critical rate line from 24 to 20), and the bad-luck case (1 in 20) from 31 to 23. Same shape as the GDD says
--    ("the chance falls as the line nears its maximum"), only the steps of the staircase moved.
--  * new_item: a piece that is assembled, bought or sent as a gift starts with a GEN set in its first GEN slot and with stat lines that grow with its set: Than Linh 1 line of
--    one step, Huy Diet 2 lines of one or two steps, Thien Su 3 lines of one to three steps (types at random, never twice the same type, never the red line).

SET NAMES utf8mb4;

UPDATE balance_config
   SET value_json = JSON_SET(value_json,
        '$.success', JSON_ARRAY(JSON_ARRAY(0.6, 100), JSON_ARRAY(0.8, 85), JSON_ARRAY(0.9, 65), JSON_ARRAY(0.97, 45), JSON_ARRAY(1.01, 30)),
        '$.new_item', JSON_OBJECT('gen_lines', 1,
                                  'tiers', JSON_OBJECT('1', JSON_OBJECT('lines', 1, 'max_steps', 1),
                                                       '2', JSON_OBJECT('lines', 2, 'max_steps', 2),
                                                       '3', JSON_OBJECT('lines', 3, 'max_steps', 3)))),
       updated_by = 'V042'
 WHERE config_key = 'stat_stone_rules';
