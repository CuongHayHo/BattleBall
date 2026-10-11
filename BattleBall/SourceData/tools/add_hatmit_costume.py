#!/usr/bin/env python3
"""Adds the Nro look of NPCs to the client catalogs as costumes that are not skins: 9022 Bà Hạt Mít (owner request 2026-10-10: she upgrades gear, as in the original game) and
9023 Cui (owner request 2026-10-11: the NPC of the dungeons) and 9024 Ông Gôhan (owner request 2026-10-11: he gives the quests).

Source (CONFIRMED, Teamobi2026 `npc_template`): row 21 Bà Hạt Mít: head part 117, body part 118, leg part 119, avatar (portrait icon) 1410; row 12 Cui: parts 48, 49, 50, avatar 639; row 0 Ông Gôhan: parts 18, 19, 20, avatar 349.
The parts come from the `part` table (SourceData/tables/json/part.json). Like Urôn (9020) and Bunma (9021) she is not a skin: not sold and not in the server's skin_template.
It is idempotent: run it again after the catalogs are rebuilt.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PART = os.path.normpath(os.path.join(HERE, "..", "tables", "json", "part.json"))
DATA = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "NroMannequin", "Data"))
# (costume id, name, head part, body part, leg part, avatar)
NPCS = [(9022, "Bà Hạt Mít", 117, 118, 119, 1410), (9023, "Cui", 48, 49, 50, 639), (9024, "Ông Gôhan", 18, 19, 20, 349)]


def load(path):
    raw = open(path, encoding="utf-8", newline="").read()
    return json.loads(raw), "\r\n" in raw


def save(path, doc, crlf):
    text = json.dumps(doc, ensure_ascii=False, indent=2)
    if crlf:
        text = text.replace("\n", "\r\n")
    open(path, "w", encoding="utf-8", newline="").write(text + ("\r\n" if crlf else "\n"))


def main():
    rows = {r["id"]: r for r in json.load(open(PART, encoding="utf-8"))["rows"]}
    pdb_path = os.path.join(DATA, "PartDatabase.json")
    pdb, crlf = load(pdb_path)
    have = {p["partId"] for p in pdb["parts"]}
    for _, _, head, body, leg, _ in NPCS:
        for pid in (head, body, leg):
            if pid in have:
                continue
            r = rows[pid]
            data = json.loads(r["DATA"]) if isinstance(r["DATA"], str) else r["DATA"]
            pdb["parts"].append({"partId": pid, "type": r["TYPE"], "images": [{"subIndex": i, "iconId": x[0], "dx": x[1], "dy": x[2]} for i, x in enumerate(data)]})
            have.add(pid)
    pdb["totalParts"] = len(pdb["parts"])
    save(pdb_path, pdb, crlf)

    cat_path = os.path.join(DATA, "CostumeCatalog.json")
    cat, crlf = load(cat_path)
    ids = {n[0] for n in NPCS}
    cat["costumes"] = [c for c in cat["costumes"] if c["id"] not in ids]
    for cid, name, head, body, leg, avatar in NPCS:
        cat["costumes"].append({"id": cid, "name": name, "gender": 3, "genderName": "Mọi Giới Tính", "iconId": avatar, "headPartId": head, "bodyPartId": body,
                                "legPartId": leg, "bodyGroup": "Standard", "bodyGroupId": 1, "isHeadOnly": False, "isMultiFrameHead": False, "headCyclePartIds": [],
                                "dyHead": 0, "charHeight": 42, "hpBarOffsetY": 49})
    if "totalCostumes" in cat:
        cat["totalCostumes"] = len(cat["costumes"])
    save(cat_path, cat, crlf)
    print("costumes", sorted(ids))


if __name__ == "__main__":
    main()
