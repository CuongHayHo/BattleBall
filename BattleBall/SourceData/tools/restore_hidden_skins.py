#!/usr/bin/env python3
"""Put the skins the owner had marked HIDDEN back into the game as SHOP skins (owner decision 2026-10-10: "tôi nhớ là có nhiều lắm mà tầm hơn 400 skin thì
phải, skin được lắp bởi 3 bộ phận, avatar thì bỏ đi" and, asked which group, "132 skin ẩn").

Only the HIDDEN skins made of three parts (head, body and leg: body group other than Component) come back: 53 hidden in round 1 and 79 hidden in round 2 = 132.
The 61 hidden avatars (body group Component) stay hidden.

What it does (re-run safe):
  1. writes SourceData/tables/seed/skin_user_edit_round3.csv = the round 2 CSV with those 79 rows changed to SHOP, plus the 53 round 1 rows (absent from round 2) as SHOP;
     rarity, price and stats are left empty, so build_skin_pricing.py gives them the data-driven rarity, the BASE_PRICE x RARITY_PRICE_MULT price and the default stats
  2. adds their costumes and parts (from SourceData/tables/backup/*_full*.json) to the CURRENT Assets/NroMannequin/Data/CostumeCatalog.json and PartDatabase.json
     (the files keep everything else, e.g. the monkey and Urôn looks that are not in the backups)
Then run build_skin_pricing.py and build_restored_skins_sql.py.
"""
import csv
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, ".."))
SEED = os.path.join(SRC, "tables", "seed")
BACKUP = os.path.join(SRC, "tables", "backup")
DATA = os.path.normpath(os.path.join(SRC, "..", "Assets", "NroMannequin", "Data"))


def read(name):
    with open(os.path.join(SEED, name), encoding="utf-8-sig", newline="") as f:
        r = csv.DictReader(f)
        return r.fieldnames, list(r)


def main():
    fields, round2 = read("skin_user_edit_round2.csv")
    _, round1 = read("skin_user_edit_2026-10-07.csv")
    ids2 = {r["costumeId"] for r in round2}
    restored = []
    out = []
    for r in round2:
        if r["kind"] == "HIDDEN" and r["bodyGroup"] != "Component":
            r = dict(r, kind="SHOP", rarity="", gemPrice="", stats="", fusionRace="", fusionOnlyMainCharacter="", rarityStatus="")
            restored.append(int(r["costumeId"]))
        out.append(r)
    for r in round1:
        if r["kind"] == "HIDDEN" and r["bodyGroup"] != "Component" and r["costumeId"] not in ids2:
            out.append(dict(r, kind="SHOP", rarity="", gemPrice="", stats="", fusionRace="", fusionOnlyMainCharacter="", rarityStatus=""))
            restored.append(int(r["costumeId"]))
    path = os.path.join(SEED, "skin_user_edit_round3.csv")
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, extrasaction="ignore")
        w.writeheader()
        w.writerows(out)
    print("round 3 CSV: %d rows, %d skins restored" % (len(out), len(restored)))

    full_cat = json.load(open(os.path.join(BACKUP, "CostumeCatalog_full414.json"), encoding="utf-8"))
    full_parts = json.load(open(os.path.join(BACKUP, "PartDatabase_full984.json"), encoding="utf-8"))
    cat_path = os.path.join(DATA, "CostumeCatalog.json")
    part_path = os.path.join(DATA, "PartDatabase.json")
    cat = json.load(open(cat_path, encoding="utf-8"))
    pdb = json.load(open(part_path, encoding="utf-8"))
    have_c = {c["id"] for c in cat["costumes"]}
    have_p = {p["partId"] for p in pdb["parts"]}
    by_id = {c["id"]: c for c in full_cat["costumes"]}
    by_part = {p["partId"]: p for p in full_parts["parts"]}
    add_c = add_p = 0
    for cid in sorted(restored):
        c = by_id[cid]
        if cid not in have_c:
            cat["costumes"].append(c)
            have_c.add(cid)
            add_c += 1
        need = [c[k] for k in ("headPartId", "bodyPartId", "legPartId") if c[k] >= 0] + [x for x in (c.get("headCyclePartIds") or []) if x >= 0]
        for pid in need:
            if pid not in have_p:
                pdb["parts"].append(by_part[pid])
                have_p.add(pid)
                add_p += 1
    pdb["parts"].sort(key=lambda p: p["partId"])
    cat["totalCostumes"] = len(cat["costumes"])
    pdb["totalParts"] = len(pdb["parts"])
    for p, obj in ((cat_path, cat), (part_path, pdb)):
        with open(p, "w", encoding="utf-8", newline="") as f:
            f.write(json.dumps(obj, ensure_ascii=False, indent=2))
    print("client catalog: +%d costumes (now %d), +%d parts (now %d)" % (add_c, cat["totalCostumes"], add_p, pdb["totalParts"]))


if __name__ == "__main__":
    main()
