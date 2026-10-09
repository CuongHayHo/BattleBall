-- BattleBallServer: the gold currency (vàng) is item #190 (owner, 2026-10-07: "#190 đi"). NRO item #190 is "Vàng" (TYPE 9, icon 930).
-- Like the gem (V018), the id and icon only name and draw the currency on the client; the amount stays in character_wallet.gold.

SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('gold_item', '{"item_id":190,"name_vi":"Vàng","nro_name":"Vàng","icon_id":930}', 'Mã vật phẩm và icon của vàng (tiền tệ)', 'owner-choice')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'owner-choice' WHERE domain = 'BALANCE';
