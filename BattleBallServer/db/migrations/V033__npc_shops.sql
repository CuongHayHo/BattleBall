-- BattleBallServer: NPC shops (owner request 2026-10-09: "thêm 1 npc urôn bán hàng (bán tất cả skin)", docs/architecture/BattleBallServer.md 15.15).
-- The server answers ShopRequest{npc_id} with the content of the shop of that NPC, the way Teamobi's Npc opens its shop (cmd -44, tabs of items).
-- balance_config 'npc_shops': one key per NPC id of the village map:
--   name:  the name shown at the top of the shop
--   tabs[]: name, gender (the skins of that race: 0 Trái Đất, 1 Namếc, 2 Xayda, 3 every race); a tab with nothing to sell is not sent
-- A shop sells every skin the skin tables allow to sell (skin_template.enabled, skin_kind.enabled and sellable, gem_price set), sorted by price then id.
SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('npc_shops', '{"uron":{"name":"Urôn","tabs":[{"name":"Mọi tộc","gender":3},{"name":"Trái Đất","gender":0},{"name":"Xayda","gender":2},{"name":"Namếc","gender":1}]}}',
   'Cửa hàng của NPC (khóa = id NPC trên bản đồ): Urôn bán mọi cải trang bán được, chia tab theo tộc', 'V033')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V033' WHERE domain = 'BALANCE';
