-- BattleBallServer: the EXP bag serves the three planets (owner request 2026-10-12: "túi kinh nghiệm dùng cho cả 3 hành tinh"; docs 15.35).
-- One bag now gives balance_config exp_bag.exp experience to EACH of the three characters of the account (EconomyService.useExpBag), not to one. This migration only fixes the texts
-- that said "one character": the note of the material, the text of the campaign mail and the mails already sent (the gift lines are not touched).
SET NAMES utf8mb4;

UPDATE material_template SET note = 'Dùng một lần cho cả ba hành tinh: mỗi nhân vật nhận đủ kinh nghiệm để đi từ cấp 1 đến cấp 100', updated_by = 'V051' WHERE template_id = 207;

UPDATE balance_config SET description = 'Túi kinh nghiệm: số kinh nghiệm một túi cho MỖI nhân vật (tổng 100 x L^2 với L = 1 đến 99), dùng một lần cho cả ba hành tinh (docs 15.35)', updated_by = 'V051' WHERE config_key = 'exp_bag';

UPDATE signup_campaign SET updated_by = 'V051',
  body = 'Tặng bạn 99 Thẻ nâng cấp kỹ năng và 1 Túi kinh nghiệm (dùng một lần cho cả 3 hành tinh, mỗi nhân vật lên cấp 100). Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.'
  WHERE campaign_id = 1;

UPDATE mail SET body = 'Tặng bạn 99 Thẻ nâng cấp kỹ năng và 1 Túi kinh nghiệm (dùng một lần cho cả 3 hành tinh, mỗi nhân vật lên cấp 100). Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.'
  WHERE sent_by IN ('V049', 'campaign:1');

UPDATE static_data_version SET version = version + 1, updated_by = 'V051' WHERE domain = 'BALANCE';
