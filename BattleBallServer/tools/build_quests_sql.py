"""Writes db/migrations/V045__quests.sql: the quests of the NPC Ông Gôhan and the skill cards (owner request 2026-10-11: "thêm NPC xử lý tạo nhiệm vụ cho người chơi (điểm danh,
đánh phó bản, lên level, mua đồ, nâng cấp đồ, học dùng đồ, học dùng bông tai, ...), mũi tên động nhỏ chỉ dẫn đến NPC cần để hoàn thành nhiệm vụ, hoàn thành nhận vàng, ngọc, thẻ
nâng cấp (số lượng thẻ dùng để nâng cấp/học 1 skill tăng dần theo danh sách skill)"; docs/architecture/BattleBallServer.md 15.32).

What it makes (every number is PROPOSED by the assistant, the owner only gave the kinds of quests and the rule of the cards):
  * material 206 "Thẻ nâng cấp" (kind SKILL_CARD): not sold, only given by quests.
  * skill_level.card_cost: the cards a learn or a raise costs, the position of the skill in the list of its planet (slot + 1: the punch 1 ... Khiên năng lượng 8, the ultimate 9),
    the same at every level. Gold is still paid (the owner did not say to drop it). 0 = no card needed (editable on the admin page).
  * table quest_progress and balance_config 'quests': a chain of tutorial quests (one at a time, done once) and daily quests (reset at 00:00 in Vietnam, UTC+7).

Quest fields: id, kind (TUTORIAL | DAILY), title, desc (what to do), type (what is counted, see QuestService), target, npc (the NPC to go to: the arrow of the client points to it; "" =
no NPC, the action is done from the panel), reward {gold_wins (of a win of the highest character: 1000 x level), gems, items [{material, qty}]}.

Run from anywhere:  python tools/build_quests_sql.py   (the result is a migration: once applied it is never edited, change the data on the admin page or with a new migration)
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'db', 'migrations', 'V045__quests.sql')

CARD = 206      # skill card
UPGRADE = 201   # upgrade stone
GEN = 204       # GEN stone
GEN_SLOT = 205  # GEN slot stone


def reward(gold_wins, gems, cards, *items):
    its = [{'material': CARD, 'qty': cards}] if cards else []
    its += [{'material': m, 'qty': q} for m, q in items]
    return {'gold_wins': gold_wins, 'gems': gems, 'items': its}


def quest(qid, kind, title, desc, qtype, target, npc, rew):
    return {'id': qid, 'kind': kind, 'title': title, 'desc': desc, 'type': qtype, 'target': target, 'npc': npc, 'reward': rew}


T, D = 'TUTORIAL', 'DAILY'
QUESTS = [
    # the chain of the first steps: one at a time, in this order; the long goals are at the end
    quest('t01', T, 'Điểm danh lần đầu', 'Gặp Ông Trưởng làng và điểm danh nhận quà hằng ngày.', 'ATTENDANCE', 1, 'elder', reward(1, 5, 3)),
    quest('t02', T, 'Học một kỹ năng', 'Mở bảng nhân vật, vào tab Kỹ năng rồi dùng thẻ nâng cấp và vàng để học hoặc nâng một kỹ năng.', 'UPGRADE_SKILL', 1, '', reward(1, 5, 4)),
    quest('t03', T, 'Mua một món đồ', 'Đến Bunma, mua hoặc đổi mảnh lấy một món trang bị.', 'GET_GEAR', 1, 'bunma', reward(2, 5, 4, (UPGRADE, 2))),
    quest('t04', T, 'Mặc trang bị', 'Mở bảng nhân vật, chọn "Xem đồ" rồi mặc món đồ vừa có vào nhân vật.', 'EQUIP_ITEM', 1, '', reward(1, 3, 3, (UPGRADE, 1))),
    quest('t05', T, 'Nâng cấp đồ', 'Đến Bà Hạt Mít, đặt món đồ và đá nâng cấp vào bảng rồi nâng cấp.', 'UPGRADE_ITEM', 1, 'shop', reward(2, 5, 4, (GEN, 2))),
    quest('t06', T, 'Đổi dòng GEN', 'Đến Bà Hạt Mít, dùng đá GEN đổi một dòng GEN của món đồ.', 'GEN_REROLL', 1, 'shop', reward(2, 5, 5, (GEN_SLOT, 1))),
    quest('t07', T, 'Tập đánh Mộc nhân', 'Đến Mộc nhân và dùng nút nắm đấm hoặc kỹ năng đánh nó.', 'TRAINING', 1, 'moc_nhan', reward(1, 3, 3)),
    quest('t08', T, 'Thắng phó bản đầu tiên', 'Đến Cui, lên tàu vào một phó bản và hạ boss để thắng.', 'DUNGEON_WIN', 1, 'cui', reward(3, 10, 6)),
    quest('t09', T, 'Lên cấp 5', 'Đánh phó bản để nhân vật mạnh nhất của tài khoản đạt cấp 5.', 'LEVEL', 5, 'cui', reward(3, 10, 6)),
    quest('t10', T, 'Mua mảnh Bông tai Potara', 'Đến Bunma, mua một mảnh Bông tai Potara (cần 100 mảnh để ghép).', 'BUY_SHARDS', 1, 'bunma', reward(2, 5, 4)),
    quest('t11', T, 'Lên cấp 10', 'Tiếp tục đánh phó bản để đạt cấp 10.', 'LEVEL', 10, 'cui', reward(5, 15, 8)),
    quest('t12', T, 'Ghép Bông tai Potara', 'Gom đủ 100 mảnh (mua ở Bunma hoặc rơi từ boss) rồi ghép Bông tai để hợp thể.', 'OWN_EARRING', 1, 'bunma', reward(10, 30, 12)),
    quest('t13', T, 'Lên cấp 50 để hợp thể', 'Hợp thể cần cả hai nhân vật đạt cấp 50. Đánh phó bản để luyện cấp.', 'LEVEL', 50, 'cui', reward(30, 100, 30, (GEN_SLOT, 2))),
    # daily quests: all five at once, they start again every day
    quest('d01', D, 'Điểm danh', 'Gặp Ông Trưởng làng và điểm danh hôm nay.', 'ATTENDANCE', 1, 'elder', reward(0.5, 1, 2)),
    quest('d02', D, 'Thắng một phó bản', 'Đến Cui và thắng một phó bản.', 'DUNGEON_WIN', 1, 'cui', reward(1, 2, 4)),
    quest('d03', D, 'Hạ 5 boss phó bản', 'Hạ tổng cộng 5 boss trong các phó bản (thắng hay thua đều tính).', 'BOSS_KILL', 5, 'cui', reward(1.5, 3, 5)),
    quest('d04', D, 'Tập đánh Mộc nhân', 'Đến Mộc nhân và đánh thử một lượt.', 'TRAINING', 1, 'moc_nhan', reward(0.25, 1, 1)),
    quest('d05', D, 'Nâng cấp một món đồ', 'Đến Bà Hạt Mít và nâng cấp một món đồ (đá nâng cấp mua ở Bunma).', 'UPGRADE_ITEM', 1, 'shop', reward(0.5, 1, 2, (UPGRADE, 1))),
    quest('d06', D, 'Nâng cấp một kỹ năng', 'Mở bảng nhân vật, tab Kỹ năng, dùng thẻ để học hoặc nâng một kỹ năng.', 'UPGRADE_SKILL', 1, '', reward(0.5, 1, 3)),
]
CONFIG = {'quests': QUESTS}


def main():
    value = json.dumps(CONFIG, ensure_ascii=False, separators=(',', ':')).replace("\\", "\\\\").replace("'", "''")      # MariaDB reads a backslash in a string as an escape
    sql = """-- BattleBallServer: the quests of Ông Gôhan and the skill cards (docs 15.32).
-- PROPOSED by the assistant: every number; cards are paid in addition to the gold of a skill; the daily quests restart at 00:00 in Vietnam (UTC+7).
SET NAMES utf8mb4;

ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL','POTARA_SHARD','GEN_STONE','GEN_SLOT_STONE','SKILL_CARD') NOT NULL;

INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by) VALUES
  (206, 'skill_card', 'SKILL_CARD', 'Thẻ nâng cấp', NULL, 'NONE', NULL, NULL, 999999, 13538, 1, 'Học hoặc nâng một kỹ năng tốn thẻ (skill_level.card_cost); chỉ nhận từ nhiệm vụ', 'V045');

ALTER TABLE skill_level ADD COLUMN card_cost SMALLINT UNSIGNED NOT NULL DEFAULT 0 AFTER gold_cost;
UPDATE skill_level l JOIN skill_template t ON t.skill_id = l.skill_id SET l.card_cost = t.slot + 1, l.updated_by = 'V045';

-- progress of the quests: the counters of the events (the quests of the types LEVEL and OWN_EARRING are read from the account, never stored).
-- period_key: '0' for a tutorial quest (done once), the day (Vietnam time, YYYY-MM-DD) for a daily quest.
CREATE TABLE IF NOT EXISTS quest_progress (
  account_id BIGINT UNSIGNED NOT NULL,
  quest_id   VARCHAR(24)     NOT NULL,
  period_key VARCHAR(10)     NOT NULL,
  progress   INT UNSIGNED    NOT NULL DEFAULT 0,
  claimed_at TIMESTAMP       NULL,
  PRIMARY KEY (account_id, quest_id, period_key),
  CONSTRAINT fk_quest_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('quests', '%s',
   'Nhiệm vụ Ông Gôhan: quests[] (id, kind TUTORIAL|DAILY, title, desc, type, target, npc, reward gold_wins/gems/items) (docs 15.32)', 'V045')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V045' WHERE domain IN ('BALANCE', 'SKILL');
""" % value
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(sql)
    print('wrote', os.path.normpath(OUT), len(QUESTS), 'quests')


main()
