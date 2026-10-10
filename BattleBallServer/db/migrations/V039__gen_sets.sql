-- BattleBallServer: GEN, the activation sets (owner request 2026-10-10: "học và đối chiếu lại src của teamobi về set kích hoạt và áp dụng lên GEN"; docs 15.21).
-- Teamobi (SetClothes): a piece carries the tag of a set, five pieces of one set worn together switch an effect of one skill on. Owner decisions: the +100% is the damage of ONE skill,
-- these 13 sets (Sôngôku, Thiên Xin Hăng, Kirin, Kaio / Picolo, Ốc tiêu, Pikkoro, Nail / Kakarot, Ca Đíc, Nappa, Ca Đíc M / Gohan), a piece may carry several GEN lines (only the sets of its
-- planet), a GEN stone changes a chosen line to another random set, a second stone opens one more line.
-- PROPOSED by the assistant, not asked (all in balance_config gen_rules / material_template, editable): one line to begin with, at most three, the second line costs 1 slot stone and the third 2,
-- a change costs 1 GEN stone, GEN stone = 1 win and slot stone = 6 wins of gold, Gohan gives +30% gold only (the luck of the original has no counterpart here), Kaio at 2 pieces gives +10%
-- critical rate (the original code adds 10/100 = 0, the text says "critical"), Picolo works as its text says (the original multiplies by 3/2 = 1).
-- Tiers: for each kind of effect the highest step reached decides the value; the effects of a lower step stay when the higher step does not mention them.
SET NAMES utf8mb4;

ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL','POTARA_SHARD','GEN_STONE','GEN_SLOT_STONE') NOT NULL;
ALTER TABLE item_log MODIFY action ENUM('DROP','BUY','ASSEMBLE','CRAFT','UPGRADE','SELL','DESTROY','REWARD','LOST_BAG_FULL','GEN_REROLL','GEN_SLOT') NOT NULL;

INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by) VALUES
  (204, 'stone_gen',      'GEN_STONE',      'Đá GEN',        NULL, 'GOLD', 1.00, NULL, 999999, 10208, 1, 'Đổi một dòng GEN của món sang một bộ ngẫu nhiên của hành tinh món đó', 'V039'),
  (205, 'stone_gen_slot', 'GEN_SLOT_STONE', 'Đá mở ô GEN',   NULL, 'GOLD', 6.00, NULL, 999999, 10209, 1, 'Mở thêm một dòng GEN cho một món (ô 2 tốn 1 đá, ô 3 tốn 2)', 'V039');

ALTER TABLE item_instance ADD COLUMN gen_extra_slots TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER upgrade_percent;

-- One row per GEN line of a piece (an empty slot has no row). line_no is 0-based and below initial_slots + gen_extra_slots.
CREATE TABLE IF NOT EXISTS item_gen_line (
  item_id  BIGINT UNSIGNED NOT NULL,
  line_no  TINYINT UNSIGNED NOT NULL,
  gen_key  VARCHAR(32)     NOT NULL,
  PRIMARY KEY (item_id, line_no),
  CONSTRAINT fk_igl_item FOREIGN KEY (item_id) REFERENCES item_instance (item_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('gen_rules', '{"initial_slots":1,"max_slots":3,"slot_unlock_stones":[1,2],"reroll_stones":1}',
   'GEN: ô ban đầu, ô tối đa, đá mở từng ô (ô 2, ô 3), đá cho một lần đổi (docs 15.21)', 'V039'),
  ('gen_sets', '{"items":[{"key":"SONGOKU","race":"EARTH","name":"Set Sôngôku","tiers":[{"pieces":5,"text":"Kamejoko +100% sát thương","effects":[{"type":"SKILL_DMG_PCT","skill":1,"value":100}]}]},{"key":"THIEN_XIN_HANG","race":"EARTH","name":"Set Thiên Xin Hăng","tiers":[{"pieces":5,"text":"Thái dương hạ san: thời gian choáng x2","effects":[{"type":"SKILL_DURATION_MULT","skill":6,"value":1}]}]},{"key":"KIRIN","race":"EARTH","name":"Set Kirin","tiers":[{"pieces":5,"text":"Quả cầu kênh khí +100% sát thương","effects":[{"type":"SKILL_DMG_PCT","skill":10,"value":100}]}]},{"key":"KAIO","race":"EARTH","name":"Set Thần Vũ Trụ Kaio","tiers":[{"pieces":2,"text":"Tăng 10% tỷ lệ chí mạng","effects":[{"type":"STAT","stat":"CRIT_RATE_PCT","value":10}]},{"pieces":4,"text":"Kaioken tốn 5% HP thay vì 10%","effects":[{"type":"SKILL_HP_COST_DELTA","skill":9,"value":-5}]},{"pieces":5,"text":"Kaioken +30% sát thương, chỉ tốn 3% HP","effects":[{"type":"SKILL_HP_COST_DELTA","skill":9,"value":-7},{"type":"SKILL_DMG_PCT","skill":9,"value":30}]}]},{"key":"PICOLO","race":"NAMEC","name":"Set Picolo","tiers":[{"pieces":5,"text":"Makankosappo +100% sát thương","effects":[{"type":"SKILL_DMG_PCT","skill":11,"value":100}]}]},{"key":"OC_TIEU","race":"NAMEC","name":"Set Ốc tiêu","tiers":[{"pieces":5,"text":"Liên hoàn +100% sát thương","effects":[{"type":"SKILL_DMG_PCT","skill":17,"value":100}]}]},{"key":"PIKKORO","race":"NAMEC","name":"Set Pikkoro Daimao","tiers":[{"pieces":5,"text":"Đẻ trứng: con không biến mất theo thời gian, sát thương x4","effects":[{"type":"MINION_PERSIST","skill":12,"value":1},{"type":"SKILL_DMG_PCT","skill":12,"value":300}]}]},{"key":"NAIL","race":"NAMEC","name":"Set Nail chiến binh Namếc","tiers":[{"pieces":2,"text":"Tăng 10% sát thương chí mạng","effects":[{"type":"STAT","stat":"CRIT_DMG_PCT","value":10}]},{"pieces":4,"text":"Masenko hồi chiêu nhanh hơn 20%","effects":[{"type":"SKILL_COOLDOWN_PCT","skill":3,"value":20}]},{"pieces":5,"text":"Masenko +80% sát thương, hồi chiêu nhanh hơn 50%","effects":[{"type":"SKILL_COOLDOWN_PCT","skill":3,"value":50},{"type":"SKILL_DMG_PCT","skill":3,"value":80}]}]},{"key":"KAKAROT","race":"XAYDA","name":"Set Kakarot","tiers":[{"pieces":5,"text":"Đấm Galick +100% sát thương","effects":[{"type":"SKILL_DMG_PCT","skill":4,"value":100}]}]},{"key":"CADIC","race":"XAYDA","name":"Set Ca Đíc","tiers":[{"pieces":5,"text":"Biến khỉ: thời gian x5","effects":[{"type":"SKILL_DURATION_MULT","skill":13,"value":4}]}]},{"key":"NAPPA","race":"XAYDA","name":"Set Nappa","tiers":[{"pieces":5,"text":"+80% HP","effects":[{"type":"STAT","stat":"HP_PCT","value":80}]}]},{"key":"CADIC_M","race":"XAYDA","name":"Set Ca Đíc M","tiers":[{"pieces":2,"text":"+20% HP, Tự phát nổ rộng thêm 200","effects":[{"type":"STAT","stat":"HP_PCT","value":20},{"type":"SKILL_RADIUS_PX","skill":14,"value":200}]},{"pieces":4,"text":"Tự phát nổ thêm 20% HP tối đa vào sát thương","effects":[{"type":"SKILL_DMG_HPMAX_PCT","skill":14,"value":20}]},{"pieces":5,"text":"Tự phát nổ thêm 50% HP tối đa vào sát thương","effects":[{"type":"SKILL_DMG_HPMAX_PCT","skill":14,"value":50}]}]},{"key":"GOHAN","race":"ALL","name":"Set Gohan","tiers":[{"pieces":5,"text":"+30% vàng nhận được","effects":[{"type":"STAT","stat":"GOLD_PCT","value":30}]}]}]}',
   'Các bộ GEN: khóa, hành tinh (EARTH, NAMEC, XAYDA, ALL), tên, các nấc theo số món đang mặc, mỗi nấc có text và danh sách hiệu ứng (STAT hoặc SKILL_*; docs 15.21)', 'V039')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V039' WHERE domain = 'BALANCE';
