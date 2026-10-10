#!/usr/bin/env python3
"""Adds the jar of Ma phong ba (Bình) to the client catalogs as a costume that is not a skin (owner request 2026-10-10: the targets of the skill become the jar).

Source (CONFIRMED, Teamobi2026): Player.getHead/getBody/getLeg: effectSkill.isBinh -> ConstPlayer idOutfitMafuba = {1218, 1219, 1220} (head, body, leg parts).
The three parts come from the `part` table (SourceData/tables/json/part.json). Like the monkey form (costumes 9001 to 9007, build_skill_forms.py) the jar is a form
drawn by the client only: it is not sold and not in the server's skin_template. Run again after the catalogs are rebuilt (it is idempotent).
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PART = os.path.normpath(os.path.join(HERE, "..", "tables", "json", "part.json"))
DATA = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "NroMannequin", "Data"))
JAR_COSTUME = 9101
HEAD, BODY, LEG = 1218, 1219, 1220


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
    for pid in (HEAD, BODY, LEG):
        if pid in have:
            continue
        r = rows[pid]
        data = json.loads(r["DATA"]) if isinstance(r["DATA"], str) else r["DATA"]
        pdb["parts"].append({"partId": pid, "type": r["TYPE"], "images": [{"subIndex": i, "iconId": x[0], "dx": x[1], "dy": x[2]} for i, x in enumerate(data)]})
    pdb["totalParts"] = len(pdb["parts"])
    save(pdb_path, pdb, crlf)

    cat_path = os.path.join(DATA, "CostumeCatalog.json")
    cat, crlf = load(cat_path)
    cat["costumes"] = [c for c in cat["costumes"] if c["id"] != JAR_COSTUME]
    cat["costumes"].append({"id": JAR_COSTUME, "name": "Bình Ma phong ba", "gender": 2, "genderName": "Xayda", "iconId": 11166, "headPartId": HEAD, "bodyPartId": BODY,
                            "legPartId": LEG, "bodyGroup": "Jar", "bodyGroupId": 5, "isHeadOnly": False, "isMultiFrameHead": False, "headCyclePartIds": [],
                            "dyHead": 0, "charHeight": 40, "hpBarOffsetY": 30})
    if "totalCostumes" in cat:
        cat["totalCostumes"] = len(cat["costumes"])
    save(cat_path, cat, crlf)
    print("jar costume", JAR_COSTUME, "parts", HEAD, BODY, LEG)


if __name__ == "__main__":
    main()
