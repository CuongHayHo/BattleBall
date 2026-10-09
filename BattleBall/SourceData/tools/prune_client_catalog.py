#!/usr/bin/env python3
"""Remove the costumes the owner marked HIDDEN from the Unity client's catalog, keeping only the costumes listed in
SourceData/tables/seed/skin_pricing.json (221 after the second review round).

Reads/writes:
  Assets/NroMannequin/Data/CostumeCatalog.json   keep only the kept costumes; update totalCostumes
  Assets/NroMannequin/Data/PartDatabase.json     keep only parts referenced by the kept costumes (head, body, leg, head cycle); update totalParts
Backups (made once, never overwritten): SourceData/tables/backup/CostumeCatalog_full414.json, PartDatabase_full984.json
Re-run safe: it always filters the CURRENT files by the seed's costume ids.
"""
import json
import os
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, ".."))
DATA = os.path.normpath(os.path.join(SRC, "..", "Assets", "NroMannequin", "Data"))
SEED = os.path.join(SRC, "tables", "seed", "skin_pricing.json")
BACKUP = os.path.join(SRC, "tables", "backup")


def main():
    keep_ids = {c["costumeId"] for c in json.load(open(SEED, encoding="utf-8"))["costumes"]}
    cat_path = os.path.join(DATA, "CostumeCatalog.json")
    part_path = os.path.join(DATA, "PartDatabase.json")
    os.makedirs(BACKUP, exist_ok=True)
    for src, name in ((cat_path, "CostumeCatalog_full414.json"), (part_path, "PartDatabase_full984.json")):
        dst = os.path.join(BACKUP, name)
        if not os.path.exists(dst):
            shutil.copyfile(src, dst)
    full_cat = json.load(open(os.path.join(BACKUP, "CostumeCatalog_full414.json"), encoding="utf-8"))
    full_parts = json.load(open(os.path.join(BACKUP, "PartDatabase_full984.json"), encoding="utf-8"))

    costumes = [c for c in full_cat["costumes"] if c["id"] in keep_ids]
    missing = keep_ids - {c["id"] for c in costumes}
    if missing:
        raise SystemExit("seed ids not in the catalog: %s" % sorted(missing)[:10])
    used = set()
    for c in costumes:
        for k in ("headPartId", "bodyPartId", "legPartId"):
            if c[k] >= 0:
                used.add(c[k])
        used.update(x for x in (c.get("headCyclePartIds") or []) if x >= 0)
    parts = [p for p in full_parts["parts"] if p["partId"] in used]
    unresolved = used - {p["partId"] for p in parts}
    if unresolved:
        raise SystemExit("parts referenced but absent from PartDatabase: %s" % sorted(unresolved)[:10])

    cat = dict(full_cat)
    cat["costumes"] = costumes
    cat["totalCostumes"] = len(costumes)
    pdb = dict(full_parts)
    pdb["parts"] = parts
    pdb["totalParts"] = len(parts)
    for path, obj in ((cat_path, cat), (part_path, pdb)):
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(obj, f, ensure_ascii=False, indent=2)
            f.write("\n")
    print("costumes: %d -> %d | parts: %d -> %d" % (len(full_cat["costumes"]), len(costumes), len(full_parts["parts"]), len(parts)))


if __name__ == "__main__":
    main()
