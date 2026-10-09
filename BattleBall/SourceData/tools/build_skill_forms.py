#!/usr/bin/env python3
"""Build the skill-activated appearance ("form") seed for the Xayda monkey skill (skill id 13 "Bien hinh").

Source of truth (verified in Teamobi2026 code):
  Player.getHead(): effectSkill.isMonkey -> ConstPlayer.HEADMONKEY[levelMonkey - 1]   (ConstPlayer.java line 5)
  Player.getBody(): isMonkey -> 193;  Player.getLeg(): isMonkey -> 194
  HEADMONKEY = {192, 195, 196, 199, 197, 200, 198}  (levels 1..7)
Frames come from the `part` table (SourceData/tables/json/part.json): each part lists sprite/icon ids.
Output: SourceData/tables/seed/skill_forms.json. These parts and icon ids are the skill form and must be EXCLUDED from
the skin (costume) catalog and shop; they are not part of CostumeCatalog.json / PartDatabase.json.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PART = os.path.normpath(os.path.join(HERE, "..", "tables", "json", "part.json"))
OUT = os.path.normpath(os.path.join(HERE, "..", "tables", "seed", "skill_forms.json"))
HEAD_BY_LEVEL = {1: 192, 2: 195, 3: 196, 4: 199, 5: 197, 6: 200, 7: 198}
BODY, LEG = 193, 194
ICON_RANGE = (2200, 2243)


def main():
    parts = {r["id"]: r for r in json.load(open(PART, encoding="utf-8"))["rows"]}

    def icons(pid):
        return [x[0] for x in parts[pid]["DATA"]]

    used = set()
    levels = []
    for L, h in HEAD_BY_LEVEL.items():
        used |= set(icons(h))
        levels.append(dict(level=L, headPartId=h, bodyPartId=BODY, legPartId=LEG, headIconIds=icons(h)))
    used |= set(icons(BODY)) | set(icons(LEG))
    in_range = sorted(i for i in used if ICON_RANGE[0] <= i <= ICON_RANGE[1])
    unref = [i for i in range(ICON_RANGE[0], ICON_RANGE[1] + 1) if i not in used]
    doc = dict(
        version=1,
        skillId=13, race="XAYDA", name="Biến hình (Hóa khỉ khổng lồ)", formName="Khỉ",
        source="Teamobi2026 Player.getHead/getBody/getLeg + ConstPlayer.HEADMONKEY",
        bodyPartId=BODY, legPartId=LEG, bodyIconIds=icons(BODY), legIconIds=icons(LEG),
        levels=levels,
        excludePartIdsFromSkinCatalog=sorted(set(HEAD_BY_LEVEL.values()) | {BODY, LEG}),
        iconRange=list(ICON_RANGE), iconIdsReferenced=in_range,
        iconIdsInRangeNotReferencedByAnyPart=unref,
        note="Icon ids 2200-2243 hold the monkey frames; 4 ids in the range are not referenced by any part (usage UNKNOWN).")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("levels:", [(l["level"], l["headPartId"]) for l in levels])
    print("referenced icons in range:", len(in_range), "unreferenced:", unref)


if __name__ == "__main__":
    main()
