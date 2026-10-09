-- BattleBallServer: constants of the match simulation (sizes, speeds, ranges, timings). PROPOSED values: the GDD fixes the rules
-- (docs/gameplay/game-design-doc.md 7.4 and 7.5, docs/protocol/BattleBall.md section 6) but not these numbers; all editable on the admin page "Cân bằng".
-- Distances are in NRO pixels, speeds in pixels per second.

SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('match_rules',
   '{"arena_w":1600,"arena_h":900,"tick_ms":50,"player_speed":220,"ally_speed":200,"boss_speed":150,"minion_speed":170,"player_radius":20,"boss_radius":34,"minion_radius":18,"boss_melee_range":60,"minion_melee_range":40,"minion_hit_interval_s":1.2,"leash_radius":700,"leash_extra":200,"leash_heal_pct_per_s":5,"boss_spawn_delay_s":3,"between_boss_heal_pct":15,"minion_wave_every_s":8,"minion_wave_size":2,"minion_alive_cap":6,"enrage_hp_pct":30,"enrage_gap_factor":0.7,"enrage_stun_cooldown_s":12,"boss_shield_s":10,"boss_stun_radius":300,"boss_stun_s":2,"boss_ultimate_telegraph_s":2,"boss_ultimate_length":500,"boss_ultimate_width":160,"boss_beam_speed":480,"boss_beam_range":700,"boss_beam_width":60,"boss_control_factor":0.3,"boss_control_max_ms":3000,"pause_max_s":30,"pauses_per_match":2,"match_max_s":1800,"revive_hp_pct":100,"regen_skill_s":15,"damage_variance_pct":5,"tier_level":{"early":15,"mid":45,"late":80}}',
   'Hằng số mô phỏng trận (kích thước sân, tốc độ, tầm, nhịp boss, enrage, giới hạn quái nhỏ, tạm dừng, cấp quy chiếu theo cấp vùng)', 'proposal')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'proposal' WHERE domain = 'BALANCE';
