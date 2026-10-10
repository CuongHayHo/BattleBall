-- BattleBallServer: three sets of gear (owner request 2026-10-10: "quần áo nên chia nhiều tab, đồ thần linh, đồ hủy diệt, đồ thiên sứ, có 1 tab đổi bằng mảnh";
-- docs/architecture/BattleBallServer.md 15.17).
-- The five pieces of the plan (GDD 6) become the first set, Thần Linh (tier 1, the base stats of the plan); Hủy Diệt (tier 2) and Thiên Sứ (tier 3) are new sets of the same five pieces.
-- Owner choice (2026-10-10): the sets get stronger by steps of x1, x1,5 and x2 of the base stats, and cost more gold (a piece = 10 fragments of 0,6 / 0,9 / 1,2 wins, i.e. 6 / 9 / 12 wins).
-- PROPOSED by the assistant, not asked: the level a character needs (1, 30, 60) and which Nro pictures stand for the pieces (Hủy Diệt 6315 to 6318 and 6323, Thiên Sứ 10165 to 10169).
-- Each set has its own five fragments (the "đổi bằng mảnh" tab of Bunma exchanges the 10 fragments of a piece for that piece). Where the new fragments drop is OPEN: no drop rule gives them yet
-- (drop_rule rows of the admin page).
-- balance_config 'npc_shops': Bunma's tabs become Thần Linh, Hủy Diệt, Thiên Sứ (sells GEAR with a tier), Đổi mảnh (sells EXCHANGE) and Đá (STONES).
SET NAMES utf8mb4;

UPDATE item_template SET name_vi = 'Áo Thần Linh', note = 'GDD 6: Áo = Giáp 2.500 (bộ Thần Linh, bậc 1)' WHERE template_id = 1;
UPDATE item_template SET name_vi = 'Quần Thần Linh', note = 'GDD 6: Quần = Máu 200.000 (bộ Thần Linh, bậc 1)' WHERE template_id = 2;
UPDATE item_template SET name_vi = 'Găng Thần Linh', note = 'GDD 6: Găng = Tấn công 20.000 (bộ Thần Linh, bậc 1)' WHERE template_id = 3;
UPDATE item_template SET name_vi = 'Giày Thần Linh', note = 'GDD 6: Giày = Ki 200.000 (bộ Thần Linh, bậc 1)' WHERE template_id = 4;
UPDATE item_template SET name_vi = 'Nhẫn Thần Linh', note = 'GDD 6: Nhẫn = +25% sát thương chí mạng (bộ Thần Linh, bậc 1)' WHERE template_id = 5;

INSERT INTO item_template (template_id, item_key, slot, name_vi, tier, base_stat_key, base_value, pieces_needed, min_level, icon_id, enabled, note, updated_by) VALUES
  (6,  'armor_destroyer',  'ARMOR',  'Áo Hủy Diệt',    2, 'FLAT_ARMOR',    3750.00,   10, 30, 6315, 1, 'Bộ Hủy Diệt (bậc 2): x1,5 chỉ số cơ bản của bộ Thần Linh', 'V037'),
  (7,  'pants_destroyer',  'PANTS',  'Quần Hủy Diệt',  2, 'FLAT_HP',       300000.00, 10, 30, 6316, 1, 'Bộ Hủy Diệt (bậc 2): x1,5', 'V037'),
  (8,  'gloves_destroyer', 'GLOVES', 'Găng Hủy Diệt',  2, 'FLAT_ATK',      30000.00,  10, 30, 6318, 1, 'Bộ Hủy Diệt (bậc 2): x1,5', 'V037'),
  (9,  'boots_destroyer',  'BOOTS',  'Giày Hủy Diệt',  2, 'FLAT_KI',       300000.00, 10, 30, 6317, 1, 'Bộ Hủy Diệt (bậc 2): x1,5', 'V037'),
  (10, 'ring_destroyer',   'RING',   'Nhẫn Hủy Diệt',  2, 'CRIT_DMG_PCT',  37.50,     10, 30, 6323, 1, 'Bộ Hủy Diệt (bậc 2): x1,5', 'V037'),
  (11, 'armor_angel',      'ARMOR',  'Áo Thiên Sứ',    3, 'FLAT_ARMOR',    5000.00,   10, 60, 10165, 1, 'Bộ Thiên Sứ (bậc 3): x2 chỉ số cơ bản của bộ Thần Linh', 'V037'),
  (12, 'pants_angel',      'PANTS',  'Quần Thiên Sứ',  3, 'FLAT_HP',       400000.00, 10, 60, 10166, 1, 'Bộ Thiên Sứ (bậc 3): x2', 'V037'),
  (13, 'gloves_angel',     'GLOVES', 'Găng Thiên Sứ',  3, 'FLAT_ATK',      40000.00,  10, 60, 10168, 1, 'Bộ Thiên Sứ (bậc 3): x2', 'V037'),
  (14, 'boots_angel',      'BOOTS',  'Giày Thiên Sứ',  3, 'FLAT_KI',       400000.00, 10, 60, 10167, 1, 'Bộ Thiên Sứ (bậc 3): x2', 'V037'),
  (15, 'ring_angel',       'RING',   'Nhẫn Thiên Sứ',  3, 'CRIT_DMG_PCT',  50.00,     10, 60, 10169, 1, 'Bộ Thiên Sứ (bậc 3): x2', 'V037');

UPDATE material_template SET name_vi = 'Mảnh Áo Thần Linh', note = 'Bộ Thần Linh. GDD 6: 10 mảnh ghép 1 món; 6 trận thắng mua đủ món' WHERE template_id = 101;
UPDATE material_template SET name_vi = 'Mảnh Quần Thần Linh' WHERE template_id = 102;
UPDATE material_template SET name_vi = 'Mảnh Găng Thần Linh' WHERE template_id = 103;
UPDATE material_template SET name_vi = 'Mảnh Giày Thần Linh' WHERE template_id = 104;
UPDATE material_template SET name_vi = 'Mảnh Nhẫn Thần Linh' WHERE template_id = 105;

INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by) VALUES
  (106, 'frag_armor_destroyer',  'FRAGMENT', 'Mảnh Áo Hủy Diệt',    6,  'GOLD', 0.90, NULL, 999999, 10197, 1, 'Bộ Hủy Diệt: 10 mảnh ghép 1 món; 9 trận thắng mua đủ món', 'V037'),
  (107, 'frag_pants_destroyer',  'FRAGMENT', 'Mảnh Quần Hủy Diệt',  7,  'GOLD', 0.90, NULL, 999999, 10198, 1, NULL, 'V037'),
  (108, 'frag_gloves_destroyer', 'FRAGMENT', 'Mảnh Găng Hủy Diệt',  8,  'GOLD', 0.90, NULL, 999999, 10200, 1, NULL, 'V037'),
  (109, 'frag_boots_destroyer',  'FRAGMENT', 'Mảnh Giày Hủy Diệt',  9,  'GOLD', 0.90, NULL, 999999, 10199, 1, NULL, 'V037'),
  (110, 'frag_ring_destroyer',   'FRAGMENT', 'Mảnh Nhẫn Hủy Diệt',  10, 'GOLD', 0.90, NULL, 999999, 10201, 1, NULL, 'V037'),
  (111, 'frag_armor_angel',      'FRAGMENT', 'Mảnh Áo Thiên Sứ',    11, 'GOLD', 1.20, NULL, 999999, 10197, 1, 'Bộ Thiên Sứ: 10 mảnh ghép 1 món; 12 trận thắng mua đủ món', 'V037'),
  (112, 'frag_pants_angel',      'FRAGMENT', 'Mảnh Quần Thiên Sứ',  12, 'GOLD', 1.20, NULL, 999999, 10198, 1, NULL, 'V037'),
  (113, 'frag_gloves_angel',     'FRAGMENT', 'Mảnh Găng Thiên Sứ',  13, 'GOLD', 1.20, NULL, 999999, 10200, 1, NULL, 'V037'),
  (114, 'frag_boots_angel',      'FRAGMENT', 'Mảnh Giày Thiên Sứ',  14, 'GOLD', 1.20, NULL, 999999, 10199, 1, NULL, 'V037'),
  (115, 'frag_ring_angel',       'FRAGMENT', 'Mảnh Nhẫn Thiên Sứ',  15, 'GOLD', 1.20, NULL, 999999, 10201, 1, NULL, 'V037');

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('npc_shops', '{"uron":{"name":"Urôn","portrait_icon":728,"tabs":[{"name":"Mọi tộc","gender":3},{"name":"Trái Đất","gender":0},{"name":"Xayda","gender":2},{"name":"Namếc","gender":1}]},"bunma":{"name":"Bunma","portrait_icon":562,"tabs":[{"name":"Thần Linh","sells":"GEAR","tier":1},{"name":"Hủy Diệt","sells":"GEAR","tier":2},{"name":"Thiên Sứ","sells":"GEAR","tier":3},{"name":"Đổi mảnh","sells":"EXCHANGE"},{"name":"Đá","sells":"STONES"}]}}',
   'Cửa hàng của NPC (khóa = id NPC trên bản đồ): Urôn bán cải trang chia tab theo tộc; Bunma bán ba bộ đồ (mỗi bộ một tab, giá bằng vàng của 10 mảnh), đổi 10 mảnh lấy món, và các loại đá', 'V037')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V037' WHERE domain = 'BALANCE';
