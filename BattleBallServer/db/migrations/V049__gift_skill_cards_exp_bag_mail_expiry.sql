-- BattleBallServer: a gift to every existing account (owner request 2026-10-12: "tặng cho tất cả tài khoản x99 thẻ nâng cấp kỹ năng và túi kinh nghiệm đủ đến lv100,
-- đến hết thứ 7 tuần này, vào hòm thư"; docs/architecture/BattleBallServer.md 15.35).
--   * mail.expires_at: a mail gift that is not taken by then is lost (NULL = no limit); the gift of this migration lasts until the end of Saturday 2026-10-17 (server time, Vietnam).
--   * the EXP bag: material 207 "Túi kinh nghiệm" (kind EXP_BAG), used on one character from the bag; it gives balance_config exp_bag.exp experience, 32,835,000 = the sum
--     of 100 x L^2 for L = 1..99, exactly what a new character needs to go from level 1 to the maximum level 100 (PROPOSED reading of "đủ đến lv100": one bag takes one character to 100).
--   * one mail for each account that exists when this migration runs (an account made later does not get it): 99 skill cards (#206) and 1 EXP bag (#207).
SET NAMES utf8mb4;

ALTER TABLE mail ADD COLUMN expires_at TIMESTAMP NULL AFTER sent_by;

ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL','POTARA_SHARD','GEN_STONE','GEN_SLOT_STONE','SKILL_CARD','EXP_BAG') NOT NULL;

INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by) VALUES
  (207, 'exp_bag', 'EXP_BAG', 'Túi kinh nghiệm', NULL, 'NONE', NULL, NULL, 999999, 7396, 1, 'Dùng cho một nhân vật: đủ kinh nghiệm để đi từ cấp 1 đến cấp 100', 'V049');

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('exp_bag', '{"exp":32835000}', 'Túi kinh nghiệm: số kinh nghiệm một túi cho một nhân vật (tổng 100 x L^2 với L = 1 đến 99) (docs 15.35)', 'V049')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

INSERT INTO mail (account_id, title, body, sent_by, expires_at)
  SELECT account_id, 'Quà tặng: 99 thẻ nâng cấp và túi kinh nghiệm',
         'Tặng bạn 99 Thẻ nâng cấp kỹ năng và 1 Túi kinh nghiệm (dùng cho một nhân vật, đủ để lên cấp 100). Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
         'V049', '2026-10-17 23:59:59'
  FROM account;

INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT mail_id, 1, 'MATERIAL', 206, 99 FROM mail WHERE sent_by = 'V049';
INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT mail_id, 2, 'MATERIAL', 207, 1 FROM mail WHERE sent_by = 'V049';

UPDATE static_data_version SET version = version + 1, updated_by = 'V049' WHERE domain = 'BALANCE';
