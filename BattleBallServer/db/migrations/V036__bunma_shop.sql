-- BattleBallServer: Bunma's shop (owner request 2026-10-10: "thêm npc bunma ... bán quần áo, tất cả các loại đá đã được liệt kê như kế hoạch", docs/architecture/BattleBallServer.md 15.17).
-- Bunma sells the five pieces of gear (Áo, Quần, Găng, Giày, Nhẫn; a whole piece for the gold of its 10 fragments) and the three stones of the plan (GDD 6): the upgrade stone and the stat
-- stone for gold, the crystal stone for gems. A tab says what it sells: sells = GEAR, STONES or SKINS (the default, by gender).
-- Pictures: the Nro small images of the fragments (the "Mảnh áo/quần/giầy/găng tay/nhẫn" of Nro), the stones (upgrade stone level 3, lucky stone level 1 for the stat stone, the multi-colour stone
-- for the crystal stone) and the pieces (the Thần Linh set: Áo 4647, Quần 4648, Giầy 4649, Găng 4650, Nhẫn 6314).
-- balance_config 'npc_shops' gains portrait_icon (Nro avatar of the NPC: Urôn 728, Bunma 562) and the bunma key.
SET NAMES utf8mb4;

UPDATE material_template SET icon_id = 10197 WHERE template_id = 101;
UPDATE material_template SET icon_id = 10198 WHERE template_id = 102;
UPDATE material_template SET icon_id = 10200 WHERE template_id = 103;
UPDATE material_template SET icon_id = 10199 WHERE template_id = 104;
UPDATE material_template SET icon_id = 10201 WHERE template_id = 105;
UPDATE material_template SET icon_id = 10204 WHERE template_id = 201;
UPDATE material_template SET icon_id = 10206 WHERE template_id = 202;
UPDATE material_template SET icon_id = 6467 WHERE template_id = 203;

UPDATE item_template SET icon_id = 4647 WHERE template_id = 1;
UPDATE item_template SET icon_id = 4648 WHERE template_id = 2;
UPDATE item_template SET icon_id = 4650 WHERE template_id = 3;
UPDATE item_template SET icon_id = 4649 WHERE template_id = 4;
UPDATE item_template SET icon_id = 6314 WHERE template_id = 5;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('npc_shops', '{"uron":{"name":"Urôn","portrait_icon":728,"tabs":[{"name":"Mọi tộc","gender":3},{"name":"Trái Đất","gender":0},{"name":"Xayda","gender":2},{"name":"Namếc","gender":1}]},"bunma":{"name":"Bunma","portrait_icon":562,"tabs":[{"name":"Quần áo","sells":"GEAR"},{"name":"Đá","sells":"STONES"}]}}',
   'Cửa hàng của NPC (khóa = id NPC trên bản đồ): Urôn bán cải trang chia tab theo tộc; Bunma bán quần áo (cả món, giá bằng vàng của 10 mảnh) và các loại đá', 'V036')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V036' WHERE domain = 'BALANCE';
