#!/usr/bin/env python3
"""Adds to the client part database (Assets/NroMannequin/Data/PartDatabase.json) the Nro parts of the bosses and small monsters of the dungeons that the pruned catalog does not
have (owner request 2026-10-11: the dungeon draws its monsters from these parts, as a costume is drawn).

Which bosses: every boss of dungeon_boss in V009__dungeons.sql plus the three Halloween monsters (boss_id 137 to 139, the small monsters of V040) that the migrations give to
the dungeons without monsters of their own. The parts come from the Nro `part` table (SourceData/tables/json/part.json), the same source as add_hatmit_costume.py.
Idempotent: parts already in the database are left alone. After it run BattleBallServer/tools/build_assets.py so that the icons of the new parts are in the asset packs.
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
PART = os.path.join(ROOT, "BattleBall", "SourceData", "tables", "json", "part.json")
DATA = os.path.join(ROOT, "BattleBall", "Assets", "NroMannequin", "Data")
V009 = os.path.join(ROOT, "BattleBallServer", "db", "migrations", "V009__dungeons.sql")
HALLOWEEN = (137, 138, 139)


def load(path):
    raw = open(path, encoding="utf-8", newline="").read()
    return json.loads(raw), "\r\n" in raw


def save(path, doc, crlf):
    text = json.dumps(doc, ensure_ascii=False, indent=2)
    if crlf:
        text = text.replace("\n", "\r\n")
    open(path, "w", encoding="utf-8", newline="").write(text + ("\r\n" if crlf else "\n"))


def boss_parts():
    sql = open(V009, encoding="utf-8").read()
    # boss_template rows: (id, 'group', 'name', 'RACE' or NULL, head, body, leg, ...)
    rows = {}
    for m in re.finditer(r"^\s*\((\d+), '([^']*)', '((?:[^']|'')*)', (?:'\w+'|NULL), (\d+), (\d+), (\d+),", sql, re.M):
        rows[int(m.group(1))] = (int(m.group(4)), int(m.group(5)), int(m.group(6)))
    # dungeon_boss rows: (dungeon, boss, 'ROLE', ...)
    start = sql.index("INSERT INTO dungeon_boss")
    used = {int(m.group(1)) for m in re.finditer(r"^\s*\(\d+, (\d+), '(?:NORMAL|MINI|FINAL|MINION)'", sql[start:], re.M)}
    used |= set(HALLOWEEN)
    parts = set()
    for b in used:
        if b in rows:
            parts.update(rows[b])
    return used, parts


def main():
    used, wanted = boss_parts()
    table = {r["id"]: r for r in json.load(open(PART, encoding="utf-8"))["rows"]}
    path = os.path.join(DATA, "PartDatabase.json")
    pdb, crlf = load(path)
    have = {p["partId"] for p in pdb["parts"]}
    added = []
    for pid in sorted(wanted - have):
        r = table.get(pid)
        if r is None:
            print("part", pid, "is not in the Nro part table")
            continue
        data = json.loads(r["DATA"]) if isinstance(r["DATA"], str) else r["DATA"]
        pdb["parts"].append({"partId": pid, "type": r["TYPE"], "images": [{"subIndex": i, "iconId": x[0], "dx": x[1], "dy": x[2]} for i, x in enumerate(data)]})
        added.append(pid)
    pdb["totalParts"] = len(pdb["parts"])
    save(path, pdb, crlf)
    print("bosses", len(used), "parts needed", len(wanted), "added", len(added), added)


if __name__ == "__main__":
    main()
