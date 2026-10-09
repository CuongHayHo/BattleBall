#!/usr/bin/env python3
"""Build the BattleBall skill seed from the NRO skill table (SourceData/tables/json/skill_template.json).

Output: SourceData/tables/seed/skills_seed.json  {"version", "SkillTemplate": [...], "SkillLevel": [...]}

BattleBall uses the NRO skill set (decision of the user, 2026-10-07); the earlier GDD-based skill mechanic is dropped.
Values are copied RAW from NRO (no rebalancing). Only these things are derived, each from verified Teamobi2026 code
(SkillService.java, Skill.java) or from the skill's own description string:
  * race from nclass_id: 0 EARTH, 1 NAMEC, 2 XAYDA; a skill that appears in all three classes (id 19) becomes COMMON.
  * kind from TYPE: 1 ATTACK, 2 ALLY_BUFF, 3 SELF, 4 SPECIAL (SkillService.useSkill switch on template.type).
  * kiCostMode from mana_use_type: 0 ABSOLUTE, 1 PERCENT_OF_MAX, 2 ALL (SkillService.canUseSkillWithMana).
  * damageMeaning from dam_info (INFERRED); UNKNOWN when the description has no '#'.
Geometry/projectile fields that NRO does not have are left null (BattleBall to fill).

BattleBall changes on top of the NRO raw data (user decisions, 2026-10-07):
  * Ki cost is divided by 10 for every skill (kiCostNro keeps the original). 'ALL' mode (Makankosappo) is unchanged.
  * Skills are upgraded with gold only: powerRequire is dropped; goldCost = NRO price; NRO uses 9999 as a
    placeholder for levels not bought with gold, those rows get goldCost=null and goldCostStatus=NEEDS_VALUE.
  * Ultimate skills (TYPE 4) have max level 9: NRO's 10th row is dropped.
`params` holds the real per-level effect values reproduced from Teamobi2026 SkillUtil.java / SkillService.java
(formula in `paramsSource`), because for many skills the `damage` column is not what the server code uses.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, "..", "tables", "json", "skill_template.json"))
OUT = os.path.normpath(os.path.join(HERE, "..", "tables", "seed", "skills_seed.json"))
VERSION = 1

RACE = {0: "EARTH", 1: "NAMEC", 2: "XAYDA"}
KIND = {1: "ATTACK", 2: "ALLY_BUFF", 3: "SELF", 4: "SPECIAL"}
KI_MODE = {0: "ABSOLUTE", 1: "PERCENT_OF_MAX", 2: "ALL"}
# BattleBall cast modes (user decision: every skill supports both aiming modes except self buff/heal)
CAST_MODES = {1: ["DIRECTION", "TARGET"], 4: ["DIRECTION", "TARGET"], 2: ["SELF"], 3: ["SELF"]}


def damage_meaning(dam_info):
    if "#" not in dam_info:
        return "UNKNOWN"
    if "mili giây" in dam_info:
        return "DURATION_MS"
    if "giây" in dam_info and "/s" not in dam_info:
        return "DURATION_SECONDS"
    if "/s" in dam_info:
        return "REGEN_PCT_PER_SEC"
    if "Phục hồi" in dam_info:
        return "HEAL_PCT"
    if "+#%HP" in dam_info:
        return "TEMP_HP_PCT"
    return "DAMAGE_PCT"


KI_DIV = 10
GOLD_PLACEHOLDER = 9999
ULTIMATE_MAX_LEVEL = 9

# damageMeaning overrides verified against code (see section 10.6 of docs/database/BattleBall.md)
MEANING_OVERRIDE = {
    6: "DURATION_MS",            # == getTimeStun (L+2)*1000
    11: "DAMAGE_PCT_OF_MAX_KI",  # NPoint.getDameAttack: maxKi * damage / 100
    10: "UNUSED_FORMULA_IN_CODE",
    12: "UNUSED_FORMULA_IN_CODE",
    13: "UNUSED_FORMULA_IN_CODE",
    14: "UNUSED_FORMULA_IN_CODE",
    18: "UNUSED_FORMULA_IN_CODE",
    19: "DURATION_SECONDS",     # == getTimeShield (L+2)*5000 ms
    20: "STUN_DURATION_MS",      # == getTimeDCTT (L+1)*500
    23: "DURATION_SECONDS",      # == getTimeTroi L*5000 ms
    24: "UNKNOWN",
    25: "UNKNOWN",
    26: "UNKNOWN",
}

SRC_UTIL = "Teamobi2026 SkillUtil.java"
SRC_SVC = "Teamobi2026 SkillService.java"


def params_for(sid, L):
    """Per-level effect values from NRO server code. Returns (dict, source) or (None, None)."""
    if sid == 6:
        return dict(stunMs=(L + 2) * 1000, rangePx=120 + L * 30), SRC_UTIL
    if sid == 7:
        return dict(healPctOfMaxHpAndKi=(L + 9) * 5, revivesDead=True, alsoHealsCaster=True,
                    extraAlliesInRadiusPx=300 if L > 1 else 0), SRC_UTIL + " getPercentTriThuong; " + SRC_SVC + " useSkillBuffToPlayer"
    if sid == 8:
        return dict(regenPctPerSec=L + 3), SRC_UTIL + " getPercentCharge (unit INFERRED)"
    if sid == 9:
        return dict(selfHpCostPctOfMax=10), SRC_SVC + " useSkillAttack (KAIOKEN)"
    if sid == 10:
        return dict(chargeMs=4000, rangePx=350 + L * 30,
                    damageFormula="10% HP of mobs/players in range + 10 x base attack"), SRC_SVC + "; NPoint.getDameAttack"
    if sid == 11:
        return dict(chargeMs=3000), SRC_SVC + " useSkillAttack (MAKANKOSAPPO)"
    if sid == 12:
        return dict(petDamagePctOfAttack=[30, 40, 50, 60, 70, 80, 90][L - 1], lifetimeMs=(L + 5) * 20000,
                    nroSummonMobTemplate=[8, 11, 32, 25, 43, 49, 50][L - 1]), SRC_UTIL + " getHPMobMe/getTimeSurviveMobMe/getTempMobMe; MobMe"
    if sid == 13:
        return dict(durationMs=(L + 5) * 10000, hpBonusPct=(L + 3) * 10, dameBonusRaw=L + 3), SRC_UTIL + " getTimeMonkey/getPercentHpMonkey/getPercentDameMonkey (dame unit INFERRED)"
    if sid == 14:
        return dict(chargeMs=2000, minHoldMs=1500, rangePx=400 + L * 30, damageFormula="current HP of caster",
                    casterDies=True, vsBossDamageDivisor=2), SRC_SVC + " useSkillAlone (TU_SAT); " + SRC_UTIL + " getRangeBom"
    if sid == 18:
        return dict(durationMs=30000), SRC_UTIL + " getTimeSocola"
    if sid == 19:
        return dict(durationMs=(L + 2) * 5000), SRC_UTIL + " getTimeShield"
    if sid == 20:
        return dict(stunMs=(L + 1) * 500, teleportsToTarget=True, alwaysCrit=True), SRC_UTIL + " getTimeDCTT; " + SRC_SVC
    if sid == 21:
        return dict(hpBonusPct=(L + 3) * 10, displayedDurationSec=30, namecAlliesLose10PctHp=True), SRC_UTIL + " getPercentHPHuytSao; " + SRC_SVC
    if sid == 22:
        return dict(sleepMs=(L + 4) * 1000), SRC_UTIL + " getTimeThoiMien"
    if sid == 23:
        return dict(holdMs=L * 5000), SRC_UTIL + " getTimeTroi (some bosses capped at 5000 ms in code)"
    if sid in (24, 25):
        return dict(gongMs=2000), SRC_SVC + " updateSkillSpecial; NewSkill.TIME_GONG"
    if sid == 26:
        return dict(gongMs=2000, rangePx=500, trapMs=4000), SRC_SVC + " updateSkillSpecial (MA_PHONG_BA); NRO 1-in-50 caster death REMOVED (PROPOSED)"
    return None, None


# ---- PROPOSED rules chosen by the assistant (user: "tu toi uu lai") -------------------------------------------
# Absolute-Ki skills -> % of max Ki. cost(L) = PEAK * kiNro(L) / max_L(kiNro). Peaks by role.
KI_PEAK_PCT = {0: 1, 2: 1, 4: 1,          # punches
               1: 5, 3: 5, 5: 5,          # beams
               9: 5,                      # Kaioken (also costs 10% HP)
               17: 3,                     # Lien hoan (very short cooldown)
               20: 8, 21: 10, 22: 6, 23: 8}
# Gold for rows where NRO has the 9999 placeholder: continue NRO's doubling if the skill has real prices,
# otherwise base by NRO slot x growth^(level-1) (growth 2, ultimates 1.5).
GOLD_BASE_BY_SLOT = {3: 1000, 4: 2000, 5: 1500, 6: 1500, 7: 1000, 8: 5000}
GOLD_GROWTH = 2.0
GOLD_GROWTH_ULTIMATE = 1.5
# Ultimates (TYPE 4): damage column used as % ATK for 24 and 25; 26 has no damage (trap only).
ULTIMATE_DAMAGE_PCT = {24, 25}
PROJECTILE_SPEED = 480      # px per second (PROPOSED; NRO dart caps at 8 px per update)
SLACK_MELEE = 12            # px
SLACK_PROJECTILE = 16       # px
BEAM_LENGTH = 300           # px, NRO Skill.RANGE_ATTACK_CHIEU_CHUONG (Skill.java)
LASER_LENGTH = 2000         # px, Makankosappo (NRO dx = 20000 = unlimited)
# shape per skill: RECT_FRONT (length x width along the aim direction), CIRCLE (radius), TARGET (single target
# within radius), HALF_CIRCLE_FRONT (radius), NONE
HIT_RULES = {
    0: "RECT_FRONT", 2: "RECT_FRONT", 4: "RECT_FRONT", 9: "RECT_FRONT", 17: "RECT_FRONT",
    1: "RECT_FRONT", 3: "RECT_FRONT", 5: "RECT_FRONT",
    6: "CIRCLE", 10: "CIRCLE", 14: "CIRCLE", 21: "CIRCLE",
    11: "RECT_FRONT", 20: "TARGET", 22: "TARGET", 23: "TARGET", 18: "TARGET", 7: "TARGET",
    12: "NONE", 13: "NONE", 8: "NONE", 19: "NONE",
    24: "RECT_FRONT", 25: "RECT_FRONT", 26: "HALF_CIRCLE_FRONT",
}


def gold_fill(slot, is_ult, rows):
    """Return {level: (goldCost, status)}."""
    known = {x["point"]: x["price"] for x in rows if x["price"] != GOLD_PLACEHOLDER}
    out = {}
    for x in rows:
        L = x["point"]
        if L in known:
            out[L] = (known[L], "NRO")
            continue
        if known:
            last = max(known)
            val = known[last] * (GOLD_GROWTH ** (L - last))
        else:
            base = GOLD_BASE_BY_SLOT[slot]
            g = GOLD_GROWTH_ULTIMATE if is_ult else GOLD_GROWTH
            val = base * (g ** (L - 1))
        step = 100 if is_ult else 50
        out[L] = (int(round(val / float(step)) * step), "PROPOSED_RULE")
    return out


def geometry(sid, x, params):
    shape = HIT_RULES.get(sid, "NONE")
    g = dict(length=None, width=None, radius=None, projectileSpeed=None, travelMs=None, slackPx=None)
    dx, dy = x["dx"], x["dy"]
    if shape == "RECT_FRONT":
        if sid in (1, 3, 5):                                  # beam projectiles
            g.update(length=BEAM_LENGTH, width=dy, projectileSpeed=PROJECTILE_SPEED,
                     travelMs=int(round(BEAM_LENGTH * 1000.0 / PROJECTILE_SPEED)), slackPx=SLACK_PROJECTILE)
        elif sid == 11:                                       # laser, hits after the charge
            g.update(length=LASER_LENGTH, width=48, travelMs=0, slackPx=SLACK_PROJECTILE)
        elif sid in (24, 25):                                 # ultimates hit after the 2 s gong
            g.update(length=dx, width=dy, travelMs=0, slackPx=SLACK_PROJECTILE)
        else:                                                 # melee
            g.update(length=dx, width=dy, travelMs=0, slackPx=SLACK_MELEE)
    elif shape == "CIRCLE":
        rad = params["rangePx"] if params and "rangePx" in params else dx
        g.update(radius=rad, slackPx=SLACK_PROJECTILE)
        if sid == 10:
            g.update(projectileSpeed=PROJECTILE_SPEED)        # thrown ball: travel time depends on distance
    elif shape == "TARGET":
        g.update(radius=dx, slackPx=SLACK_MELEE)
    elif shape == "HALF_CIRCLE_FRONT":
        g.update(radius=params["rangePx"], travelMs=0, slackPx=SLACK_PROJECTILE)
    return shape, g


def main():
    src = json.load(open(SRC, encoding="utf-8"))
    by_id = {}
    for r in src["rows"]:
        e = by_id.setdefault(r["id"], {"row": r, "classes": []})
        e["classes"].append(r["nclass_id"])
        # identical skill rows across classes must be identical (id 19 case)
        if e["row"]["skills"] != r["skills"] or e["row"]["NAME"] != r["NAME"]:
            raise SystemExit("skill %s differs between classes" % r["id"])

    templates, levels = [], []
    for sid in sorted(by_id):
        r = by_id[sid]["row"]
        classes = sorted(by_id[sid]["classes"])
        race = "COMMON" if len(classes) == 3 else RACE[classes[0]]
        is_ult = r["TYPE"] == 4
        rows = [x for x in r["skills"] if not (is_ult and x["point"] > ULTIMATE_MAX_LEVEL)]
        max_level = max(x["point"] for x in rows)
        meaning = MEANING_OVERRIDE.get(sid, damage_meaning(r["dam_info"]))
        mode = KI_MODE[r["mana_use_type"]]
        shape = HIT_RULES.get(sid, "NONE")
        max_ki_nro = max(x["mana_use"] for x in rows) or 1
        if any(x["price"] == GOLD_PLACEHOLDER for x in rows):
            gold = gold_fill(r["slot"], is_ult, rows)
        else:
            gold = {x["point"]: (x["price"], "NRO") for x in rows}
        templates.append(dict(
            skillId=sid, race=race, name=r["NAME"], nroType=r["TYPE"], kind=KIND[r["TYPE"]],
            kiCostModeNro=mode, kiCostMode="ALL" if mode == "ALL" else "PERCENT_OF_MAX", damInfo=r["dam_info"],
            damageMeaning=meaning, castModes=CAST_MODES[r["TYPE"]], hitShape=shape,
            iconId=r["icon_id"], slot=r["slot"], nroMaxPoint=r["max_point"], maxLevel=max_level,
            version=VERSION, source="NRO skill_template"))
        for x in rows:
            L = x["point"]
            params, psrc = params_for(sid, L)
            ki_nro = x["mana_use"]
            if mode == "ALL":
                ki_pct, ki_rule = None, "ALL (consumes all Ki)"
            elif mode == "PERCENT_OF_MAX":
                ki_pct, ki_rule = round(ki_nro / float(KI_DIV), 1), "NRO percent / 10 (user rule)"
            else:
                ki_pct = round(KI_PEAK_PCT[sid] * ki_nro / float(max_ki_nro), 2)
                ki_rule = "absolute->percent: peak %d%% x kiNro/max (PROPOSED)" % KI_PEAK_PCT[sid]
            if ki_pct is not None and ki_pct == int(ki_pct):
                ki_pct = int(ki_pct)
            g_cost, g_status = gold[L]
            _, geo = geometry(sid, x, params)
            dmg_pct = x["damage"] if (meaning == "DAMAGE_PCT" or sid in ULTIMATE_DAMAGE_PCT) else None
            levels.append(dict(
                skillId=sid, level=L, nroLevelId=x["id"],
                damage=x["damage"], damagePctAtk=dmg_pct,
                damagePctMaxKi=x["damage"] if sid == 11 else None,
                dx=x["dx"], dy=x["dy"],
                kiCostPct=ki_pct, kiCostNro=ki_nro, kiCostRule=ki_rule,
                cooldownMs=x["cool_down"], maxFight=x["max_fight"],
                goldCost=g_cost, goldCostStatus=g_status, info=x["info"],
                params=params, paramsSource=psrc,
                length=geo["length"], width=geo["width"], radius=geo["radius"],
                projectileSpeed=geo["projectileSpeed"], travelMs=geo["travelMs"], slackPx=geo["slackPx"],
                version=VERSION, status="NRO_RAW"))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    doc = {"version": VERSION, "source": "Teamobi2026/SRC/sql/nro1.sql skill_template (via tables/json)",
           "SkillTemplate": templates, "SkillLevel": levels}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    from collections import Counter
    print("templates:", len(templates), "levels:", len(levels))
    print("by race:", dict(Counter(t["race"] for t in templates)))
    print("maxLevel:", dict(Counter(t["maxLevel"] for t in templates)))
    print("goldCostStatus:", dict(Counter(r["goldCostStatus"] for r in levels)))
    print("kiCostRule:", dict(Counter(r["kiCostRule"].split(":")[0] for r in levels)))
    print("rows with params:", sum(1 for r in levels if r["params"]))


if __name__ == "__main__":
    main()
