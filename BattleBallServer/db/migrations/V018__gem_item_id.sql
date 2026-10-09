-- BattleBallServer: the gem currency (đá quý) is item #674 (owner, 2026-10-07: "ID: #674 là id của đá quý").
-- In NRO item #674 is "Đá ngũ sắc" (TYPE 27, icon 6467). BattleBall shows the gem with this id and icon; the amount stays in character_wallet.gems.
-- The id is only used by the client/protocol to name the currency (shop price tags, reward messages); it is never an item_instance.

SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('gem_item', '{"item_id":674,"name_vi":"Đá quý","nro_name":"Đá ngũ sắc","icon_id":6467}', 'Mã vật phẩm và icon của đá quý (tiền tệ)', 'owner-choice')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'owner-choice' WHERE domain = 'BALANCE';
