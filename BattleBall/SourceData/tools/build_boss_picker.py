"""Generates BattleBallServer/admin/boss_group_picker.html: a SELF-CONTAINED page where the owner assigns one NRO boss group to each BattleBall
dungeon, with every boss rendered in the IDLE pose (same maths as build_skin_browser.py).

Sources (read-only): Teamobi2026/SRC/src/nro/models/boss/** and boss_con_duong_ran_doc/** (BossData definitions, outfit part ids),
                     SourceData/tables/json/part.json, SourceData/data/icon/x2/<iconId>.png.
A boss group = a folder of the NRO boss code (e.g. doanh_trai = Red Ribbon HQ). Safe to re-run; overwrites the HTML only.
"""
import base64
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.dirname(HERE)                                   # BattleBall/SourceData
ROOT = os.path.normpath(os.path.join(SRC, "..", ".."))        # E:\PRJNRO
BOSS_DIR = os.path.join(ROOT, "Teamobi2026", "SRC", "src", "nro", "models", "boss")
SNAKE_DIR = os.path.join(ROOT, "Teamobi2026", "SRC", "src", "nro", "models", "boss_con_duong_ran_doc")
PART = os.path.join(SRC, "tables", "json", "part.json")
OUT = os.path.join(ROOT, "BattleBallServer", "admin", "boss_group_picker.html")
DEFAULT_BODY, DEFAULT_LEG = 0, 0

# folder -> (Vietnamese label, tag). tag: DUNGEON = a dungeon in NRO, BOSS = ordinary boss group, EVENT = seasonal event, SPECIAL = needs other systems
GROUPS = {
    "doanh_trai": ("Doanh trại độc nhãn (Red Ribbon)", "DUNGEON"),
    "khi_gas": ("Khí gas huỷ diệt", "DUNGEON"),
    "ban_do_kho_bau": ("Bản đồ kho báu", "DUNGEON"),
    "con_duong_ran_doc": ("Con đường rắn độc", "DUNGEON"),
    "yardrat": ("Hành tinh Yardrat (chiến binh)", "BOSS"),
    "Nappa": ("Nhóm Nappa (Kuku, Mập Đầu Đinh, Rambo)", "BOSS"),
    "Android": ("Android và Dr. Kôrê", "BOSS"),
    "Cell": ("Cell và Xên con", "BOSS"),
    "Broly": ("Broly", "BOSS"),
    "Cold": ("Cooler", "BOSS"),
    "Frieza": ("Fide (Frieza)", "BOSS"),
    "Golden_fireza": ("Fide Vàng", "BOSS"),
    "Baby": ("Baby", "BOSS"),
    "Black_Goku": ("Black Goku", "BOSS"),
    "cumber": ("Cumber", "BOSS"),
    "MajinBuu_12h": ("Majin Bư (nhóm 12h)", "BOSS"),
    "MajinBuu_14h": ("Majin Bư (nhóm 14h)", "BOSS"),
    "trai_dat": ("Bojack và thuộc hạ (Trái Đất)", "BOSS"),
    "tieu_doi_sat_thu": ("Tiểu đội sát thủ", "BOSS"),
    "tieu_doi_sat_thu_namek": ("Tiểu đội sát thủ Namek", "BOSS"),
    "vo_dai_hat_mit": ("Võ đài Hắt Mít", "BOSS"),
    "ma_vuong_picolo": ("Ma vương Pocolo", "BOSS"),
    "Tau_PayPay": ("Tàu Pảy Pảy", "BOSS"),
    "Boss_mini": ("Boss mini (Rồng Nhí, Sói hẹc quyn, ...)", "BOSS"),
    "dai_hoi_vo_thuat": ("Đại hội võ thuật", "SPECIAL"),
    "luyen_tap_tu_dong": ("Luyện tập (Karin, Thượng đế, ...)", "SPECIAL"),
    "sieu_hang": ("Siêu hạng (Rival, đấu với bản sao)", "SPECIAL"),
    "nhan_ban": ("Nhân bản", "SPECIAL"),
    "event": ("Quái nhỏ Halloween (Bí ma, Dơi, Ma trơi)", "MOB"),   # owner 2026-10-07: swarm mobs, not bosses
    "event_hung_vuong": ("Sự kiện Hùng Vương", "EVENT"),
    "event_noel": ("Sự kiện Noel", "EVENT"),
    "event_tet": ("Sự kiện Tết", "EVENT"),
    "event_trung_thu": ("Sự kiện Trung thu", "EVENT"),
}
GENDER = {"TRAI_DAT": "Trái Đất", "NAMEC": "Namếc", "XAYDA": "Xayda", "0": "Trái Đất", "1": "Namếc", "2": "Xayda"}


def strip_comments(text):
    out = []
    for line in text.split("\n"):
        in_str, cut = False, None
        i = 0
        while i < len(line):
            ch = line[i]
            if ch == "\\" and in_str:
                i += 2
                continue
            if ch == '"':
                in_str = not in_str
            elif ch == "/" and not in_str and line[i:i + 2] == "//":
                cut = i
                break
            i += 1
        out.append(line if cut is None else line[:cut])
    return "\n".join(out)


def split_top(s):
    parts, depth, cur, in_str, i = [], 0, [], False, 0
    while i < len(s):
        ch = s[i]
        if in_str:
            cur.append(ch)
            if ch == "\\":
                cur.append(s[i + 1])
                i += 1
            elif ch == '"':
                in_str = False
        elif ch == '"':
            in_str = True
            cur.append(ch)
        elif ch in "({[":
            depth += 1
            cur.append(ch)
        elif ch in ")}]":
            depth -= 1
            cur.append(ch)
        elif ch == "," and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
        i += 1
    parts.append("".join(cur).strip())
    return parts


def bossdata_calls(text):
    """Yield (name, gender, outfit[], dame|None, hp|None) for each `new BossData(...)` with literal name and numeric outfit."""
    text = strip_comments(text)
    for m in re.finditer(r"new BossData\(", text):
        i = m.end()
        depth, j, in_str = 1, i, False
        while j < len(text) and depth > 0:
            ch = text[j]
            if in_str:
                if ch == "\\":
                    j += 1
                elif ch == '"':
                    in_str = False
            elif ch == '"':
                in_str = True
            elif ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            j += 1
        args = split_top(text[i:j - 1])
        if len(args) < 5:
            continue
        nm = re.match(r'^"(.*)"$', args[0], re.S)
        of = re.search(r"new short\[\]\s*\{([^}]*)\}", args[2])
        if not of:
            continue
        try:
            outfit = [int(x.strip()) for x in of.group(1).split(",") if x.strip()]
        except ValueError:
            continue
        gender = re.sub(r"^.*\.", "", args[1].strip())
        dame = int(args[3]) if re.fullmatch(r"-?\d+", args[3].strip()) else None
        hpm = re.search(r"\{\s*(\d+)", args[4])
        yield (nm.group(1) if nm else None), GENDER.get(gender, gender), outfit, dame, int(hpm.group(1)) if hpm else None


def fix_name(b, filename):
    """Name from the code when it is a readable literal, else the Java class name (some names are variables or blank)."""
    name = (b[0] or "").strip()
    if len(name) < 2 or name == "$":
        name = os.path.splitext(filename)[0] + " (tên trong code: " + (name or "trống") + ")"
    return (name,) + tuple(b[1:])


def main():
    consts = {}
    big = open(os.path.join(BOSS_DIR, "BossesData.java"), encoding="utf-8", errors="ignore").read()
    for m in re.finditer(r"public static final BossData (\w+)\s*=\s*new BossData\(", big):
        chunk = big[m.start():]
        got = next(bossdata_calls(chunk), None)
        if got:
            consts[m.group(1)] = got

    groups = {}

    def add(folder, boss):
        lst = groups.setdefault(folder, [])
        key = (boss[0], tuple(boss[2][:3]))
        if all((b[0], tuple(b[2][:3])) != key for b in lst):
            lst.append(boss)

    for root, _dirs, files in os.walk(BOSS_DIR):
        rel = os.path.relpath(root, BOSS_DIR)
        folder = rel.split(os.sep)[0]
        if folder in (".", "Boss_Manager"):
            continue
        for f in files:
            if not f.endswith(".java"):
                continue
            text = open(os.path.join(root, f), encoding="utf-8", errors="ignore").read()
            for b in bossdata_calls(text):
                add(folder, fix_name(b, f))
            for c in re.findall(r"BossesData\.(\w+)", text):
                if c in consts:
                    add(folder, fix_name(consts[c], c))
    for f in os.listdir(SNAKE_DIR):
        if f.endswith(".java"):
            for b in bossdata_calls(open(os.path.join(SNAKE_DIR, f), encoding="utf-8", errors="ignore").read()):
                add("con_duong_ran_doc", fix_name(b, f))

    # Extra group (owner request 2026-10-07): the Xayda "Hóa khỉ" (Biến khỉ) skill forms, levels 1-7, as a boss group.
    # Parts from docs/database/BattleBall.md 10.13: head by level, body 193 and legs 194 fixed (Player.getHead/getBody/getLeg of Teamobi2026).
    monkey_heads = [192, 195, 196, 199, 197, 200, 198]
    groups["hoa_khi_xayda"] = [("Khỉ đột cấp %d" % (i + 1), "Xayda", [h, 193, 194], None, None) for i, h in enumerate(monkey_heads)]
    GROUPS["hoa_khi_xayda"] = ("Hóa khỉ của Xayda (khỉ đột cấp 1 đến 7)", "BOSS")

    parts = {r["id"]: r["DATA"] for r in json.load(open(PART, encoding="utf-8"))["rows"]}

    def frame(pid, sub):
        d = parts.get(pid)
        if pid is None or pid < 0 or not d or sub >= len(d):
            return None
        icon, dx, dy = d[sub]
        return [icon, dx, dy]

    out_groups = []
    for folder, bosses in groups.items():
        label, tag = GROUPS.get(folder, (folder, "BOSS"))
        items = []
        for name, gender, outfit, dame, hp in bosses:
            head, body, leg = (outfit + [-1, -1, -1])[:3]
            items.append(dict(name=name, g=gender, outfit=[head, body, leg], dame=dame, hp=hp,
                              hint=("minion_elite" if name.startswith("Xên con") else ("minion" if tag == "MOB" else "")),
                              parts=dict(head=frame(head, 0), leg=frame(leg, 1), body=frame(body, 1))))
        out_groups.append(dict(key=folder, label=label, tag=tag, bosses=items))
    order = {"DUNGEON": 0, "BOSS": 1, "MOB": 2, "SPECIAL": 3, "EVENT": 4}
    out_groups.sort(key=lambda g: (order.get(g["tag"], 9), g["label"]))

    icons = sorted({f[0] for g in out_groups for b in g["bosses"] for f in b["parts"].values() if f})
    imgs, missing = {}, []
    for i in icons:
        p = os.path.join(SRC, "data", "icon", "x2", "%d.png" % i)
        if os.path.exists(p):
            imgs[i] = base64.b64encode(open(p, "rb").read()).decode("ascii")
        else:
            missing.append(i)

    # Also keep the parsed boss groups as data for the dungeon seed (tables/seed/boss_groups.json).
    with open(os.path.join(SRC, "tables", "seed", "boss_groups.json"), "w", encoding="utf-8") as f:
        json.dump(dict(source="Teamobi2026 boss code (read-only), see build_boss_picker.py", groups=[
            dict(key=g["key"], label=g["label"], tag=g["tag"], bosses=[
                dict(name=b["name"], race=b["g"], head=b["outfit"][0], body=b["outfit"][1], leg=b["outfit"][2], nroDame=b["dame"], nroHp=b["hp"]) for b in g["bosses"]])
            for g in out_groups]), f, ensure_ascii=False, indent=1)

    blob = json.dumps(dict(groups=out_groups), ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    tpl = open(os.path.join(HERE, "boss_picker_template.html"), encoding="utf-8").read()
    html = tpl.replace("__DATA__", blob).replace("__IMGS__", json.dumps(imgs, separators=(",", ":")))
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    total = sum(len(g["bosses"]) for g in out_groups)
    noimg = [b["name"] for g in out_groups for b in g["bosses"] if not all(b["parts"].values())]
    print("wrote", OUT, "(%d KB)" % (len(html) // 1024), "| groups:", len(out_groups), "| bosses:", total, "| icons:", len(imgs),
          "| missing icon files:", missing[:5], "| bosses missing a slot:", len(noimg), noimg[:8])


if __name__ == "__main__":
    main()
