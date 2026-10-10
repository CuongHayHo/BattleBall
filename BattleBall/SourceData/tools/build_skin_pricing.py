#!/usr/bin/env python3
"""Build the PROPOSED skin (costume) classification, stats and pricing seed (v3).

Inputs : Assets/NroMannequin/Data/CostumeCatalog.json (414 costumes; costume id == NRO item_template.id, all TYPE 5)
         SourceData/tables/json/item_template.json, item_shop.json, item_shop_option.json, item_option_template.json
         SourceData/tables/seed/skin_stat_catalog.json  (run build_skin_stats.py first)
         SourceData/tables/seed/skin_user_edit_round2.csv  (the owner's latest choices exported from skin_browser.html)
Output : SourceData/tables/seed/skin_pricing.json

Kinds:
  SHOP         sold for gems; any character of the matching race can wear it.
  FUSION_FORM  Potara appearance (td/xd/nm ...). May be bought, but it is only worn by the MAIN character while fused with the
               Potara earring; AI characters (and the fused secondary) can never wear a FUSION_FORM skin.
  COMPONENT    head/face/avatar parts; not sold as skins.
  SKILL_FORM   appearance activated by a skill; not sold (none in the 414 catalog, see build_skill_forms.py).
  BOSS_MOB     appearance reserved for bosses and small monsters; not player-wearable, not sold.
  NPC          appearance reserved for NPCs (shop, quest...); not player-wearable, not sold.
  HIDDEN       the owner marked it unused: such costumes are REMOVED from this seed (and from skin_browser.html);
               a costume missing from the owner's latest CSV export counts as removed too.
The owner's CSV (kind, rarity, price) overrides every default below. Defaults for rows the CSV leaves empty (for example the new
FUSION_FORM rows) use the data-driven rarity (see rarity_of) and the BASE_PRICE x RARITY_PRICE_MULT rule.
Stats: every SHOP/FUSION_FORM skin gets default stats [ATK_PCT, HP_PCT, KI_PCT] = RARITY_STAT_BONUS; `nroStats` lists what NRO
itself attached to the costume (mapped to BattleBall stat keys) for reference.
"""
import csv
import json
import os
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
TABLES = os.path.normpath(os.path.join(HERE, "..", "tables", "json"))
SEEDDIR = os.path.normpath(os.path.join(HERE, "..", "tables", "seed"))
# the full 414 NRO costumes (the client catalog keeps only the ones in use, plus looks that are not NRO costumes)
CATALOG = os.path.normpath(os.path.join(HERE, "..", "tables", "backup", "CostumeCatalog_full414.json"))
USER_CSV = os.path.join(SEEDDIR, "skin_user_edit_round3.csv")   # round 2 export of skin_browser.html with the hidden 3-part skins put back (restore_hidden_skins.py, 2026-10-10)
STAT_CAT = os.path.join(SEEDDIR, "skin_stat_catalog.json")
OUT = os.path.join(SEEDDIR, "skin_pricing.json")
VERSION = 3

BASE_PRICE = {"Standard": 100, "Chibi": 150, "Fusion": 300, "Broly": 400, "GreatApe": 400}
RARITY_PRICE_MULT = {"COMMON": 1, "RARE": 2, "VERY_RARE": 4}
RARITY_STAT_BONUS = {"COMMON": 1.0, "RARE": 3.0, "VERY_RARE": 5.0}   # % per stat, additive bucket
DEFAULT_STAT_KEYS = ["ATK_PCT", "HP_PCT", "KI_PCT"]
KEYWORDS = ["VIP", "Siêu", "Super", "God", "Gogeta", "Rose", "SSJ", "Zamasu", "Hatchiyack", "Hợp Thể"]
SELLABLE = ("SHOP", "FUSION_FORM")


def load(name):
    return json.load(open(os.path.join(TABLES, name + ".json"), encoding="utf-8"))


def event_text(desc):
    d = desc.lower()
    return any(k in d for k in ("vpsk", "vật phẩm sự kiện", "giáng sinh", "biến mất"))


def round10(x):
    return int(round(x / 10.0) * 10)


def main():
    cat = json.load(open(CATALOG, encoding="utf-8"))["costumes"]
    item = {r["id"]: r for r in load("item_template")["rows"]}
    opt_name = {r["id"]: r["NAME"] for r in load("item_option_template")["rows"]}
    stat_cat = json.load(open(STAT_CAT, encoding="utf-8"))
    key_of = {i: s["key"] for s in stat_cat["stats"] for i in s["nroOptionIds"]}
    pct_keys = {s["key"] for s in stat_cat["stats"] if s["unit"] == "pct" and s["applies"] == "YES"}
    shops = defaultdict(list)
    for r in load("item_shop")["rows"]:
        shops[r["temp_id"]].append(r)
    opts = defaultdict(list)
    for r in load("item_shop_option")["rows"]:
        opts[r["item_shop_id"]].append((r["option_id"], r["param"]))
    user = {int(r["costumeId"]): r for r in csv.DictReader(open(USER_CSV, encoding="utf-8-sig"))}

    rows, removed = [], []
    for e in cat:
        cid = e["id"]
        u = user.get(cid)
        if u is None or u["kind"] == "HIDDEN":   # absent from the owner's latest export = removed in an earlier round
            removed.append(cid)
            continue
        desc = item[cid]["description"]
        sold = cid in shops
        bonus, gems, nro_stats = 0, [], {}
        for s in shops.get(cid, []):
            tot = 0
            for oid, p in opts[s["id"]]:
                k = key_of.get(oid)
                if k in pct_keys:
                    tot += p
                if k and p:
                    nro_stats[k] = max(nro_stats.get(k, 0), p)
            bonus = max(bonus, tot)
            if s["type_sell"] == 1 and s["cost"] > 0:
                gems.append(s["cost"])
        gem = min(gems) if gems else None
        kind = u["kind"]
        row = dict(costumeId=cid, name=e["name"], gender=e["gender"], genderName=e["genderName"],
                   bodyGroup=e["bodyGroup"], iconId=e["iconId"], kind=kind,
                   nro=dict(description=desc, soldInNroShop=sold, nroStatBonusSum=bonus, nroGemCost=gem,
                            eventItem=(not sold) and event_text(desc)),
                   nroStats=[dict(type=k, nroValue=v) for k, v in sorted(nro_stats.items())],
                   version=VERSION)
        if kind in SELLABLE:
            score = (2 if bonus >= 60 else 1 if bonus >= 20 else 0)
            score += (2 if (gem or 0) >= 1000 else 1 if (gem or 0) >= 300 else 0)
            score += 1 if row["nro"]["eventItem"] else 0
            score += 2 if any(k.lower() in e["name"].lower() for k in KEYWORDS) else 0
            auto = "VERY_RARE" if score >= 3 else "RARE" if score >= 1 else "COMMON"
            rar = (u.get("rarity") or auto) if u else auto
            price = int(u["gemPrice"]) if (u and u.get("gemPrice")) else round10(BASE_PRICE[e["bodyGroup"]] * RARITY_PRICE_MULT[rar])
            val = RARITY_STAT_BONUS[rar]
            stats = [dict(type=k, value=val) for k in DEFAULT_STAT_KEYS]
            if u and u.get("stats"):                  # "TYPE=value;TYPE=value" exported by skin_browser.html
                stats = [dict(type=a.split("=")[0], value=float(a.split("=")[1])) for a in u["stats"].split(";") if "=" in a]
            row.update(rarity=rar, rarityScore=score,
                       rarityStatus="MANUAL" if (u and u.get("rarityStatus") == "MANUAL") else "HEURISTIC",
                       gemPrice=price, statBonusPct=stats[0]["value"] if stats else 0, stats=stats)
            if kind == "FUSION_FORM":
                row.update(fusionOnlyMainCharacter=True, aiCanWear=False,
                           fusionRace=({"Trái Đất": "td", "Xayda": "xd", "Namếc": "nm"}.get(e["genderName"])
                                       or (e["name"].strip().lower() if e["name"].strip().lower() in ("td", "xd", "nm") else None)
                                       or "ANY"))   # no race in the data = any race's main character may wear it (owner decision)
        else:
            row.update(rarity=None, rarityScore=None, rarityStatus=None, gemPrice=None, statBonusPct=None, stats=[])
        rows.append(row)

    doc = {"version": VERSION, "source": "CostumeCatalog.json + NRO item data + owner CSV 2026-10-07",
           "basePrice": BASE_PRICE, "rarityPriceMult": RARITY_PRICE_MULT, "rarityStatBonusPct": RARITY_STAT_BONUS,
           "defaultStatKeys": DEFAULT_STAT_KEYS, "removedHiddenCostumeIds": removed, "costumes": rows}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("kept:", len(rows), "removed (HIDDEN):", len(removed), "| kinds:", dict(Counter(r["kind"] for r in rows)))
    sell = [r for r in rows if r["kind"] in SELLABLE]
    print("sellable by group:", dict(Counter(r["bodyGroup"] for r in sell)))
    print("sellable by rarity:", dict(Counter(r["rarity"] for r in sell)))
    print("fusion forms:", [(r["costumeId"], r["name"], r["fusionRace"], r["gemPrice"]) for r in rows if r["kind"] == "FUSION_FORM"])
    print("gem total to buy all sellable:", sum(r["gemPrice"] for r in sell))


if __name__ == "__main__":
    main()
