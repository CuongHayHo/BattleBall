-- BattleBallServer: the daily check-in (docs 15.25). The day changes at 00:00 in Vietnam (UTC+7), like the shop of Potara shards.
-- PROPOSED by the assistant: a day missed cannot be claimed later; one claim a day for the whole account; the numbers of balance_config 'attendance'.
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS attendance_claim (
  account_id BIGINT UNSIGNED NOT NULL,
  claim_date DATE            NOT NULL,                       -- the day (Vietnam time) of the check-in
  claimed_at TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id, claim_date),
  CONSTRAINT fk_attendance_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('attendance', '{"gold_wins":0.25,"gems":3,"days":[{"day":1,"items":[{"material":201,"qty":1}]},{"day":3,"items":[{"material":201,"qty":2}]},{"day":5,"items":[{"material":204,"qty":1}]},{"day":7,"gems":5,"items":[{"material":201,"qty":3}]},{"day":10,"items":[{"material":205,"qty":1}]},{"day":12,"items":[{"material":204,"qty":2}]},{"day":14,"gems":10,"items":[{"material":201,"qty":5}]},{"day":17,"items":[{"material":204,"qty":2}]},{"day":20,"items":[{"material":205,"qty":1}]},{"day":21,"gems":10,"items":[{"material":201,"qty":5}]},{"day":24,"items":[{"material":204,"qty":3}]},{"day":28,"gems":20,"items":[{"material":205,"qty":2}]}]}',
   'Điểm danh hằng ngày: mỗi ngày gold_wins trận thắng vàng và gems ngọc; days[]: phần thưởng thêm của ngày đó trong tháng (gems, gold_wins, items = mã vật liệu và số lượng) (docs 15.25)', 'V044')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V044' WHERE domain = 'BALANCE';
