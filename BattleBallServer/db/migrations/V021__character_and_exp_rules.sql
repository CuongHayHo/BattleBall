-- BattleBallServer: constants of the character stat curve and of EXP (docs/database/BattleBall.md 4.1 and 4.2, CONFIRMED by the owner), so the
-- server reads them from data instead of code. The admin page "Cân bằng" edits them.

SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('char_stats_lv100', '{"hp":575000,"ki":575000,"atk":26000,"armor":22000,"level_step_pct":1.0,"crit_dmg_pct_per_level":1.0,"crit_rate_pct_per_level":0.1,"max_level":100}',
   'Chỉ số gốc của nhân vật ở cấp 100; mỗi cấp tăng 1% chỉ số cấp 1 (tuyến tính); chí mạng theo cấp', 'seed'),
  ('exp_rules', '{"coef":100,"power":2,"wins_per_level":3,"win_bonus":0.2,"overlevel_free":5,"overlevel_step":0.05,"overlevel_floor":0.1,"underlevel_free":10,"underlevel_step":0.02,"underlevel_cap":0.2,"streak_window_s":5,"streak_tiers":[[10,0.05],[25,0.10],[50,0.15],[100,0.20]]}',
   'EXP: cần 100 x L^2 để lên cấp L+1; mỗi boss = EXP cấp R / (1,2 x số boss x 3); thắng +20%; hệ số vượt cấp, thấp cấp, kill streak', 'seed'),
  ('inventory_rules', '{"bag_limit":2000,"bag_warn":1800,"bag_page_size":100,"revives_per_match":2,"revive_gold_wins":[0.5,1.0]}',
   'Túi tối đa 2.000 món, cảnh báo ở 1.800; hồi sinh tối đa 2 lần mỗi trận (0,5 và 1 trận thắng vàng)', 'seed')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'seed' WHERE domain = 'BALANCE';
