#!/usr/bin/env python3
"""Build the catalog of stats a skin (costume) can carry, verified against the NRO server source.

Evidence:
  * Teamobi2026 src/nro/models/player/NPoint.java  addOption(ItemOption)  (lines ~472-650): the complete list of item option ids the
    server turns into character stats; equipped costumes go through exactly this function (calPoint loops itemsBody -> addOption).
  * SourceData/tables/json/item_shop_option.json + item_option_template.json: the options NRO actually attaches to the costumes
    (63 distinct option ids on the 414-costume catalog; counts and value ranges are measured, not assumed).
Output: SourceData/tables/seed/skin_stat_catalog.json  (+ printed summary)
BattleBall stat keys are PROPOSED; `applies` says whether the stat can be used in single-player BattleBall:
  YES    direct stat on the character          EFFECT  needs a dedicated mechanic (flag/aura), not a number
  NO     not applicable (other players, time limit, trading, pet, ...)
"""
import json
import os
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
TABLES = os.path.normpath(os.path.join(HERE, "..", "tables", "json"))
OUT = os.path.normpath(os.path.join(HERE, "..", "tables", "seed", "skin_stat_catalog.json"))
CATALOG = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "NroMannequin", "Data", "CostumeCatalog.json"))

# key, Vietnamese name, unit, NRO option ids (addOption cases), applies, suggested max per skin, note
STATS = [
    ("ATK_PCT", "Sức đánh %", "pct", [49, 50, 147], "YES", 10, "tlDame"),
    ("HP_PCT", "HP %", "pct", [77], "YES", 10, "tlHp"),
    ("KI_PCT", "Ki %", "pct", [103], "YES", 10, "tlMp"),
    ("ARMOR_PCT", "Giáp % (giảm thương)", "pct", [94], "YES", 10, "tlGiap; tên NRO 'Giảm #% sát thương'"),
    ("CRIT_RATE_PCT", "Tỷ lệ chí mạng (điểm %)", "pct", [14], "YES", 3, "critAdd"),
    ("CRIT_DMG_PCT", "Sát thương chí mạng %", "pct", [5], "YES", 20, "tlSDCM / tlDameCrit"),
    ("DODGE_PCT", "Né đòn %", "pct", [108, 111], "YES", 5, "tlNeDon"),
    ("REFLECT_PCT", "Phản sát thương %", "pct", [97], "YES", 5, "tlPST"),
    ("MOVE_SPEED_PCT", "Tốc độ di chuyển %", "pct", [16, 114, 148], "YES", 10, "tlSpeed; trong trận bay nên áp cho bay"),
    ("LIFESTEAL_PCT", "Biến % sát thương thành HP", "pct", [95], "YES", 3, "tlHutHp"),
    ("KI_STEAL_PCT", "Biến % sát thương thành Ki", "pct", [96], "YES", 3, "tlHutMp"),
    ("ATK_VS_MOB_PCT", "Sức đánh % khi đánh quái nhỏ", "pct", [19], "YES", 10, "tlDameAttMob"),
    ("LIFESTEAL_VS_MOB_PCT", "Biến % sát thương lên quái thành HP", "pct", [104], "YES", 3, "tlHutHpMob"),
    ("ARMOR_PIERCE_BEAM_PCT", "Xuyên giáp khi dùng chưởng %", "pct", [98], "YES", 10, "tlxgc"),
    ("ARMOR_PIERCE_MELEE_PCT", "Xuyên giáp khi cận chiến %", "pct", [99], "YES", 10, "tlxgcc"),
    ("BEAM_TO_KI_PCT", "Biến % sát thương chưởng nhận vào thành Ki", "pct", [3], "YES", 20, "voHieuChuong"),
    ("HP_REGEN_PCT", "Hồi HP % mỗi giây (NRO: mỗi 30 giây)", "pct", [80, 173], "YES", 1, "tlHpHoi; quy đổi mỗi giây"),
    ("KI_REGEN_PCT", "Hồi Ki % mỗi giây (NRO: mỗi 30 giây)", "pct", [81, 162], "YES", 2, "tlMpHoi / mpHoiCute"),
    ("EXP_PCT", "EXP từ boss %", "pct", [88, 101], "YES", 5, "tlTNSM; BattleBall chỉ có EXP theo boss"),
    ("GOLD_PCT", "Vàng nhận được %", "pct", [100], "YES", 5, "tlGold"),
    ("FLAT_ATK", "Sức đánh cộng thẳng", "flat", [0], "YES", 1000, "dameAdd"),
    ("FLAT_HP", "HP cộng thẳng", "flat", [6, 22, 2, 48], "YES", 20000, "hpAdd"),
    ("FLAT_KI", "Ki cộng thẳng", "flat", [7, 23, 2, 48], "YES", 20000, "mpAdd"),
    ("FLAT_ARMOR", "Giáp cộng thẳng", "flat", [47], "YES", 500, "defAdd"),
    ("EFFECT_STEALTH", "Tàng hình / vô hình", "flag", [25, 105], "EFFECT", None, "isTanHinh / wearingVoHinh"),
    ("EFFECT_SLOW_AURA", "Làm chậm mọi người xung quanh", "flag", [24], "EFFECT", None, "isLamCham"),
    ("EFFECT_PETRIFY_AURA", "Hóa đá xung quanh", "flag", [26], "EFFECT", None, "isHoaDa"),
    ("EFFECT_CHOCOLATE_AURA", "Biến sôcôla/kẹo xung quanh", "flag", [29, 186], "EFFECT", None, "hiệu ứng skin"),
    ("EFFECT_COLD_IMMUNE", "Không ảnh hưởng bởi cái lạnh", "flag", [106], "EFFECT", None, "isKhongLanh (map lạnh NRO: BattleBall chưa có)"),
    ("EFFECT_STUN_IMMUNE", "Kháng Thái Dương Hạ San (miễn choáng)", "flag", [116], "EFFECT", None, "khangTDHS"),
    ("EFFECT_TELEPORT", "Dịch chuyển tức thời", "flag", [33], "EFFECT", None, "teleport"),
    ("EFFECT_EXPLODE_ON_DEATH", "% phát nổ sau khi chết", "pct", [153], "EFFECT", None, "tlBom"),
    ("LEECH_AROUND_PCT", "Hút % HP, Ki của kẻ địch xung quanh mỗi 5 giây", "pct", [8], "YES", 2, "tlHutHpMpXQ"),
    ("FLAT_HP_REGEN", "Hồi HP cố định (NRO: mỗi 30 giây)", "flat", [27], "YES", 1000, "hpHoiAdd"),
    ("FLAT_KI_REGEN", "Hồi Ki cố định (NRO: mỗi 30 giây)", "flat", [28], "YES", 1000, "mpHoiAdd"),
    ("EFFECT_KI_ON_HIT", "Hồi % Ki khi bị đánh", "pct", [4], "EFFECT", None, "tên NRO 'Hồi phục #% KI khi bị đánh' (cách xử lý nằm ngoài addOption)"),
    ("EFFECT_HP_PENALTY", "Giảm % HP (nhược điểm)", "pct", [109], "EFFECT", None, "tlHpGiamODo"),
    ("EFFECT_ATK_STACK", "Cộng dồn sức đánh khi chỉ dùng chiêu đấm", "pct", [156], "EFFECT", None, "tên theo DB; lưu ý code chú thích id 156 là 'Giảm 50% sức đánh...' (lệch DB)"),
    ("EFFECT_ARMOR_LOW_KI", "Giảm % mọi sát thương khi Ki dưới 20%", "pct", [157], "EFFECT", None, "xử lý ngoài addOption"),
    ("EFFECT_DEBUFF_BONUS", "Giảm 50% sức đánh, HP, Ki đổi lấy % EXP và vàng", "pct", [155], "EFFECT", None, "tlSubSD (code gán vào case 156)"),
    ("NA_CRYSTAL_DETECT", "Dò pha lê", "other", [110], "NO", None, "isDoSPL"),
    ("NA_LUCK", "May mắn", "other", [236], "NO", None, "BattleBall không có chỉ số may mắn"),
    ("NA_LABELS", "Nhãn hiển thị (tên nhân vật, dòng chỉ số ẩn)", "other", [51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 210], "NO", None, "chỉ là chữ hiển thị"),
    ("NA_TIME_LIMIT", "Hạn sử dụng # ngày", "other", [93, 231], "NO", None, "skin BattleBall mua bằng đá quý, vĩnh viễn"),
    ("NA_BOUND", "Không thể bán/giao dịch", "other", [154, 30], "NO", None, "không có giao dịch người chơi"),
    ("NA_FUSION_ONLY", "Chỉ có tác dụng khi hợp thể", "other", [38], "NO", None, "thay bằng loại FUSION_FORM của BattleBall"),
    ("NA_NEARBY_PLAYERS", "Hiệu ứng khi ở gần người chơi/cải trang khác", "other", [117, 145, 146, 150, 177, 179, 193, 196, 226], "NO", None, "game chơi đơn, không có người chơi khác"),
    ("NA_PET", "Thưởng cho đệ tử/sư phụ", "other", [160], "NO", None, "BattleBall không có hệ đệ tử"),
    ("NA_SET", "Mảnh bộ (Tinh/Nguyệt/Nhật ấn, ...)", "other", [34, 35, 36, 211], "NO", None, "bộ đồ NRO"),
]


def load(n):
    return json.load(open(os.path.join(TABLES, n + ".json"), encoding="utf-8"))


def main():
    names = {r["id"]: r["NAME"] for r in load("item_option_template")["rows"]}
    shop = {r["id"]: r for r in load("item_shop")["rows"]}
    cat_ids = {e["id"] for e in json.load(open(CATALOG, encoding="utf-8"))["costumes"]}
    use = defaultdict(list)
    for o in load("item_shop_option")["rows"]:
        s = shop.get(o["item_shop_id"])
        if s and s["temp_id"] in cat_ids:
            use[o["option_id"]].append(o["param"])
    key_of = {}
    out = []
    for key, vi, unit, ids, applies, cap, ref in STATS:
        for i in ids:
            key_of.setdefault(i, key)
        vals = [v for i in ids for v in use.get(i, [])]
        out.append(dict(key=key, nameVi=vi, unit=unit, applies=applies, suggestedMaxPerSkin=cap,
                        nroOptionIds=ids, nroOptionNames=[names.get(i) for i in ids],
                        nroCodeRef="NPoint.addOption: " + ref,
                        nroCostumeEntries=len(vals), nroValueMin=min(vals) if vals else None, nroValueMax=max(vals) if vals else None))
    unmapped = sorted(i for i in use if i not in key_of)
    doc = dict(version=1,
               source="Teamobi2026 NPoint.addOption + item_shop_option (414 costumes)",
               note="NRO option ids used by the costumes that no stat key covers are listed in unmappedNroOptions "
                    "(cosmetic labels, character names, display text).",
               stats=out, unmappedNroOptions=[dict(id=i, name=names.get(i), entries=len(use[i])) for i in unmapped])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    from collections import Counter
    print("stat keys:", len(out), dict(Counter(s["applies"] for s in out)))
    print("NRO option ids used by costumes:", len(use), "| mapped:", len(use) - len(unmapped), "| unmapped:", len(unmapped))
    print("unmapped:", [(i, names.get(i)) for i in unmapped])


if __name__ == "__main__":
    main()
