"""Writes db/migrations/V039__gen_sets.sql: the GEN sets (the activation sets of Teamobi, docs/architecture/BattleBallServer.md 15.21), their rules, the two stones and the tables.

Run from anywhere:  python tools/build_gen_sql.py   (the result is a migration: once applied it is never edited, change the data on the admin page or with a new migration)
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'db', 'migrations', 'V039__gen_sets.sql')


def stat(stat_key, value):
    return {'type': 'STAT', 'stat': stat_key, 'value': value}


def skill(kind, skill_id, value):
    return {'type': kind, 'skill': skill_id, 'value': value}


def tier(pieces, text, *effects):
    return {'pieces': pieces, 'text': text, 'effects': list(effects)}


# Skill ids are the Nro skill template ids of skill_template: 1 Kamejoko, 3 Masenko, 4 Galick (the punch), 6 Thái dương hạ san, 9 Kaioken, 10 Quả cầu kênh khí,
# 11 Makankosappo, 12 Đẻ trứng, 13 Biến hình, 14 Tự phát nổ, 17 Liên hoàn.
SETS = [
    # Trái Đất
    {'key': 'SONGOKU', 'race': 'EARTH', 'name': 'Set Sôngôku', 'tiers': [
        tier(5, 'Kamejoko +100% sát thương', skill('SKILL_DMG_PCT', 1, 100))]},
    {'key': 'THIEN_XIN_HANG', 'race': 'EARTH', 'name': 'Set Thiên Xin Hăng', 'tiers': [
        tier(5, 'Thái dương hạ san: thời gian choáng x2', skill('SKILL_DURATION_MULT', 6, 1))]},
    {'key': 'KIRIN', 'race': 'EARTH', 'name': 'Set Kirin', 'tiers': [
        tier(5, 'Quả cầu kênh khí +100% sát thương', skill('SKILL_DMG_PCT', 10, 100))]},
    {'key': 'KAIO', 'race': 'EARTH', 'name': 'Set Thần Vũ Trụ Kaio', 'tiers': [
        tier(2, 'Tăng 10% tỷ lệ chí mạng', stat('CRIT_RATE_PCT', 10)),
        tier(4, 'Kaioken tốn 5% HP thay vì 10%', skill('SKILL_HP_COST_DELTA', 9, -5)),
        tier(5, 'Kaioken +30% sát thương, chỉ tốn 3% HP', skill('SKILL_HP_COST_DELTA', 9, -7), skill('SKILL_DMG_PCT', 9, 30))]},
    # Namếc
    {'key': 'PICOLO', 'race': 'NAMEC', 'name': 'Set Picolo', 'tiers': [
        tier(5, 'Makankosappo +100% sát thương', skill('SKILL_DMG_PCT', 11, 100))]},
    {'key': 'OC_TIEU', 'race': 'NAMEC', 'name': 'Set Ốc tiêu', 'tiers': [
        tier(5, 'Liên hoàn +100% sát thương', skill('SKILL_DMG_PCT', 17, 100))]},
    {'key': 'PIKKORO', 'race': 'NAMEC', 'name': 'Set Pikkoro Daimao', 'tiers': [
        tier(5, 'Đẻ trứng: con không biến mất theo thời gian, sát thương x4', skill('MINION_PERSIST', 12, 1), skill('SKILL_DMG_PCT', 12, 300))]},
    {'key': 'NAIL', 'race': 'NAMEC', 'name': 'Set Nail chiến binh Namếc', 'tiers': [
        tier(2, 'Tăng 10% sát thương chí mạng', stat('CRIT_DMG_PCT', 10)),
        tier(4, 'Masenko hồi chiêu nhanh hơn 20%', skill('SKILL_COOLDOWN_PCT', 3, 20)),
        tier(5, 'Masenko +80% sát thương, hồi chiêu nhanh hơn 50%', skill('SKILL_COOLDOWN_PCT', 3, 50), skill('SKILL_DMG_PCT', 3, 80))]},
    # Xayda
    {'key': 'KAKAROT', 'race': 'XAYDA', 'name': 'Set Kakarot', 'tiers': [
        tier(5, 'Đấm Galick +100% sát thương', skill('SKILL_DMG_PCT', 4, 100))]},
    {'key': 'CADIC', 'race': 'XAYDA', 'name': 'Set Ca Đíc', 'tiers': [
        tier(5, 'Biến khỉ: thời gian x5', skill('SKILL_DURATION_MULT', 13, 4))]},
    {'key': 'NAPPA', 'race': 'XAYDA', 'name': 'Set Nappa', 'tiers': [
        tier(5, '+80% HP', stat('HP_PCT', 80))]},
    {'key': 'CADIC_M', 'race': 'XAYDA', 'name': 'Set Ca Đíc M', 'tiers': [
        tier(2, '+20% HP, Tự phát nổ rộng thêm 200', stat('HP_PCT', 20), skill('SKILL_RADIUS_PX', 14, 200)),
        tier(4, 'Tự phát nổ thêm 20% HP tối đa vào sát thương', skill('SKILL_DMG_HPMAX_PCT', 14, 20)),
        tier(5, 'Tự phát nổ thêm 50% HP tối đa vào sát thương', skill('SKILL_DMG_HPMAX_PCT', 14, 50))]},
    # cả ba hành tinh
    {'key': 'GOHAN', 'race': 'ALL', 'name': 'Set Gohan', 'tiers': [
        tier(5, '+30% vàng nhận được', stat('GOLD_PCT', 30))]},
]

RULES = {'initial_slots': 1, 'max_slots': 3, 'slot_unlock_stones': [1, 2], 'reroll_stones': 1}


def sql_text(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(',', ':')).replace("'", "''")


HEADER = """-- BattleBallServer: GEN, the activation sets (owner request 2026-10-10: "học và đối chiếu lại src của teamobi về set kích hoạt và áp dụng lên GEN"; docs 15.21).
-- Teamobi (SetClothes): a piece carries the tag of a set, five pieces of one set worn together switch an effect of one skill on. Owner decisions: the +100% is the damage of ONE skill,
-- these 13 sets (Sôngôku, Thiên Xin Hăng, Kirin, Kaio / Picolo, Ốc tiêu, Pikkoro, Nail / Kakarot, Ca Đíc, Nappa, Ca Đíc M / Gohan), a piece may carry several GEN lines (only the sets of its
-- planet), a GEN stone changes a chosen line to another random set, a second stone opens one more line.
-- PROPOSED by the assistant, not asked (all in balance_config gen_rules / material_template, editable): one line to begin with, at most three, the second line costs 1 slot stone and the third 2,
-- a change costs 1 GEN stone, GEN stone = 1 win and slot stone = 6 wins of gold, Gohan gives +30% gold only (the luck of the original has no counterpart here), Kaio at 2 pieces gives +10%
-- critical rate (the original code adds 10/100 = 0, the text says "critical"), Picolo works as its text says (the original multiplies by 3/2 = 1).
-- Tiers: for each kind of effect the highest step reached decides the value; the effects of a lower step stay when the higher step does not mention them.
SET NAMES utf8mb4;

ALTER TABLE material_template MODIFY kind ENUM('FRAGMENT','UPGRADE_STONE','STAT_STONE','CRYSTAL_STONE','BOOK_MATERIAL','POTARA_SHARD','GEN_STONE','GEN_SLOT_STONE') NOT NULL;
ALTER TABLE item_log MODIFY action ENUM('DROP','BUY','ASSEMBLE','CRAFT','UPGRADE','SELL','DESTROY','REWARD','LOST_BAG_FULL','GEN_REROLL','GEN_SLOT') NOT NULL;

INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by) VALUES
  (204, 'stone_gen',      'GEN_STONE',      'Đá GEN',        NULL, 'GOLD', 1.00, NULL, 999999, 10208, 1, 'Đổi một dòng GEN của món sang một bộ ngẫu nhiên của hành tinh món đó', 'V039'),
  (205, 'stone_gen_slot', 'GEN_SLOT_STONE', 'Đá mở ô GEN',   NULL, 'GOLD', 6.00, NULL, 999999, 10209, 1, 'Mở thêm một dòng GEN cho một món (ô 2 tốn 1 đá, ô 3 tốn 2)', 'V039');

ALTER TABLE item_instance ADD COLUMN gen_extra_slots TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER upgrade_percent;

-- One row per GEN line of a piece (an empty slot has no row). line_no is 0-based and below initial_slots + gen_extra_slots.
CREATE TABLE IF NOT EXISTS item_gen_line (
  item_id  BIGINT UNSIGNED NOT NULL,
  line_no  TINYINT UNSIGNED NOT NULL,
  gen_key  VARCHAR(32)     NOT NULL,
  PRIMARY KEY (item_id, line_no),
  CONSTRAINT fk_igl_item FOREIGN KEY (item_id) REFERENCES item_instance (item_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

"""


def main():
    body = ("INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES\n"
            "  ('gen_rules', '" + sql_text(RULES) + "',\n"
            "   'GEN: ô ban đầu, ô tối đa, đá mở từng ô (ô 2, ô 3), đá cho một lần đổi (docs 15.21)', 'V039'),\n"
            "  ('gen_sets', '" + sql_text({'items': SETS}) + "',\n"
            "   'Các bộ GEN: khóa, hành tinh (EARTH, NAMEC, XAYDA, ALL), tên, các nấc theo số món đang mặc, mỗi nấc có text và danh sách hiệu ứng (STAT hoặc SKILL_*; docs 15.21)', 'V039')\n"
            "  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);\n\n"
            "UPDATE static_data_version SET version = version + 1, updated_by = 'V039' WHERE domain = 'BALANCE';\n")
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(HEADER + body)
    print('wrote', os.path.normpath(OUT), len(SETS), 'sets')


main()
