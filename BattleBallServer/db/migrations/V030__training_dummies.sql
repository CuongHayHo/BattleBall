-- BattleBallServer: training dummies of the starter village (owner decision 2026-10-08, docs/architecture/BattleBallServer.md 15.12).
-- Two enemies that never fight back, with 2 billion HP that refills when it runs out, to test damage and skills; the whole squad fights them,
-- the server computes every hit (the match engine in training mode: no bosses, no time limit, no reward, nothing recorded).
-- balance_config 'training':
--   dummies[]: key (the client places the dummy where its village map puts this key), name, hp, mob_template_id (an Nro mob, 0 = Mộc nhân;
--              -1 = drawn with costume_id), costume_id, radius (hit radius in px), armor_pct (share of damage removed, 0 = none)
--   unlearned_skill_level: skills of the race the character has not learned yet are usable at this level while training (0 = only learned skills)
--   revive_after_s: a character that dies while training (Tự phát nổ) stands up again after this many seconds with full HP and Ki
--   idle_end_s: the session ends after this many seconds without any input from the client
SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('training', '{"dummies":[{"key":"moc_nhan","name":"Mộc nhân","hp":2000000000,"mob_template_id":0,"costume_id":0,"radius":24,"armor_pct":0},{"key":"saibamen","name":"Saibamen","hp":2000000000,"mob_template_id":-1,"costume_id":647,"radius":20,"armor_pct":0}],"unlearned_skill_level":1,"revive_after_s":2,"idle_end_s":300}',
   'Luyện tập ở làng tân thủ: NPC không đánh lại, 2 tỷ máu, hết máu hồi đầy; skill chưa học dùng ở cấp unlearned_skill_level khi luyện tập', 'V030')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V030' WHERE domain = 'BALANCE';
