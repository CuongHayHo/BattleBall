"""Writes db/migrations/V044__attendance.sql: the daily check-in of the village elder (owner request 2026-10-11: "thay đổi popup NPC trưởng làng, nội dung nhận vàng ngọc
điểm danh, một bảng điểm danh ngày/tháng nhận item free"; docs/architecture/BattleBallServer.md 15.25).

Every day of the month the account may check in once and gets gold and gems; some days of the month also give free items. The rewards are data
(balance_config 'attendance', editable on the admin page): every day gives `gold_wins` of a win of gold (a win gives 1000 x level of the highest character) and `gems` gems;
an entry of `days` adds more on that day of the month (extra gems, extra gold in wins, items = material_template ids with a quantity).
PROPOSED by the assistant, not asked: every number below (the owner only said gold, gems, a table by day and month, and free items).

Run from anywhere:  python tools/build_attendance_sql.py   (the result is a migration: once applied it is never edited, change the data on the admin page or with a new migration)
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'db', 'migrations', 'V044__attendance.sql')

# material ids: 201 upgrade stone, 204 GEN stone, 205 GEN slot stone
DAYS = [
    {'day': 1, 'items': [{'material': 201, 'qty': 1}]},
    {'day': 3, 'items': [{'material': 201, 'qty': 2}]},
    {'day': 5, 'items': [{'material': 204, 'qty': 1}]},
    {'day': 7, 'gems': 5, 'items': [{'material': 201, 'qty': 3}]},
    {'day': 10, 'items': [{'material': 205, 'qty': 1}]},
    {'day': 12, 'items': [{'material': 204, 'qty': 2}]},
    {'day': 14, 'gems': 10, 'items': [{'material': 201, 'qty': 5}]},
    {'day': 17, 'items': [{'material': 204, 'qty': 2}]},
    {'day': 20, 'items': [{'material': 205, 'qty': 1}]},
    {'day': 21, 'gems': 10, 'items': [{'material': 201, 'qty': 5}]},
    {'day': 24, 'items': [{'material': 204, 'qty': 3}]},
    {'day': 28, 'gems': 20, 'items': [{'material': 205, 'qty': 2}]},
]
CONFIG = {'gold_wins': 0.25, 'gems': 3, 'days': DAYS}


def main():
    value = json.dumps(CONFIG, ensure_ascii=False, separators=(',', ':')).replace("'", "''")
    sql = """-- BattleBallServer: the daily check-in (docs 15.25). The day changes at 00:00 in Vietnam (UTC+7), like the shop of Potara shards.
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
  ('attendance', '%s',
   'Điểm danh hằng ngày: mỗi ngày gold_wins trận thắng vàng và gems ngọc; days[]: phần thưởng thêm của ngày đó trong tháng (gems, gold_wins, items = mã vật liệu và số lượng) (docs 15.25)', 'V044')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V044' WHERE domain = 'BALANCE';
""" % value
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(sql)
    print('wrote', os.path.normpath(OUT))


main()
