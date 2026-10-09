#!/usr/bin/env python3
"""Balance and economy reference model for BattleBall (PROPOSED values, chosen by the assistant on 2026-10-07).

Prints the tables used in docs/database/BattleBall.md section 10.9 and docs/gameplay/game-design-doc.md sections 6-7.
Every constant at the top is a design knob: change it and re-run.

Inputs read from the repository: SourceData/tables/seed/skills_seed.json (skill gold costs, damage, cooldowns).
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SEED = os.path.normpath(os.path.join(HERE, "..", "tables", "seed", "skills_seed.json"))

# ---- constants ----------------------------------------------------------------------------------------------
MAX_ATK_TOTAL = 500_000          # design constant kept from the GDD example
UPGRADE_MAX_PCT = 0.75           # U: max upgrade-stone bonus on a gear piece's base stat (15 steps of +5%) PROPOSED
ARMOR_K = 50_000                 # player armor -> damage reduction: A / (A + K) PROPOSED
# damage reduction (GDD armor column midpoints) of the target class, used to set HP so the TTK holds after armor
TARGET_ARMOR = {"mob": 0.075, "elite": 0.175, "boss_early": 0.225, "boss_mid": 0.34, "boss_late": 0.44,
                "miniboss": 0.325, "dungeon_final": 0.485, "raid": 0.60}
GOLD_PER_WIN_PER_LEVEL = 1_000   # Gm(R) = 1000 x R gold from a won match at recommended level R (drop-table target)
LOSS_GOLD_FACTOR = 0.5           # gold on a lost match (pro rata by bosses defeated, rough)
WINS_PER_LEVEL = 3               # docs: M = 3 wins per level at recommended level
# combat model
PUNCH_ATK_PER_SEC = 3.5          # avg of NRO punch skills at max level: damage% / cooldown (3.2, 3.88, 3.4)
UPTIME = 0.5                     # fraction of time the team actually lands hits (movement, dodging, mechanics)
CRIT_MULT = 1.1                  # 10% crit rate x +100% crit damage at level 100
AI_EFFICIENCY = 0.9              # blended: 1 player + 2 AI
TEAM_DPS_PER_ATK = PUNCH_ATK_PER_SEC * UPTIME * CRIT_MULT * AI_EFFICIENCY   # ~1.73 damage per second per ATK point
# expected team ATK as a fraction of Max_ATK_Total at each progression tier
ATK_FRACTION = {"early": 0.20, "mid": 0.45, "late": 0.85, "endgame": 1.00}
# target time-to-kill (seconds) for a team of the expected strength
TTK = {"mob": 0.4, "elite": 2.0, "boss_early": 45, "boss_mid": 75, "boss_late": 120,
       "miniboss": 40, "dungeon_final": 150, "raid": 360}
# potara
POTARA_SHARDS = 100
POTARA_SHARD_PRICE = 15_000
POTARA_DAILY_CAP = 20
# shop multipliers (x Gm(R))
ITEM_SHARDS_PER_ITEM = 10
ITEM_BUY_WINS = 6.0              # full item bought with gold ~ 6 won matches at its tier
SELL_FRACTION = 0.05             # sell price = 5% of the full shop price
UPGRADE_STONE_WINS = 1.5
STAT_STONE_WINS = 1.0
TIMED_ITEM_WINS = 0.1
REVIVE_WINS = (0.5, 1.0)


def gm(R):
    return GOLD_PER_WIN_PER_LEVEL * R


def num(x):
    return "{:,.0f}".format(x).replace(",", ".")


# ---- boss rhythm model (PROPOSED) --------------------------------------------------------------------------
KI_REGEN_PCT_PER_SEC = 5.0       # user: "hoi nhieu Ki/s"; % of max Ki per second (HP regen stays 0.5%/s while Ki > 0)
# boss actions in priority order: (name, cooldown s, damage multiplier x boss ATK, avoidable, note)
BOSS_ACTIONS = [
    ("ultimate",  60.0, 3.0, True,  "2 s telegraphed zone (Super Kamejoko style)"),
    ("shield",    45.0, 0.0, None,  "Khien nang luong, no damage"),
    ("stun",      25.0, 0.5, True,  "Thai Duong Ha San style area stun, avoid by distance"),
    ("focus",     15.0, 1.0, False, "target-lock skill on the highest-HP unit, cannot be dodged"),
    ("beam",       6.0, 1.5, True,  "Kamejoko style projectile, dodge by moving"),
    ("punch",      0.0, 1.0, True,  "basic punch, melee range"),
]
# progression tiers for the boss budget check: (player level, Quan HP factor, HP% from stones/gem/GEN, armor reduction of players)
BOSS_TIERS = {
    "boss_early": (30, 1.00, 0.0, 0.25),
    "boss_mid": (60, 1.25, 0.5, 0.30),
    "boss_late": (90, 1.60, 1.0, 0.40),
    "raid": (100, 1.75, 1.75, 0.50),
}
BOSS_ATK_PCT = {"boss_early": 0.24, "boss_mid": 0.40, "boss_late": 0.575, "raid": 0.80}
BUDGET_RATIO_IF_NO_DODGE = 2.0   # boss would deal 2x the team HP pool over the TTK if nobody dodged, healed or shielded


def simulate_mix(gap, horizon=600.0):
    """Global action gap `gap` seconds; each action picks the first ready entry in priority order. Returns multiplier per second
    split into (avoidable, unavoidable) and action counts."""
    ready = {a[0]: 0.0 for a in BOSS_ACTIONS}
    t, tot_av, tot_un, counts = 0.0, 0.0, 0.0, {}
    while t < horizon:
        for name, cd, mult, avoid, _ in BOSS_ACTIONS:
            if ready[name] <= t:
                ready[name] = t + cd
                counts[name] = counts.get(name, 0) + 1
                if avoid is False:
                    tot_un += mult
                else:
                    tot_av += mult
                break
        t += gap
    return tot_av / horizon, tot_un / horizon, counts


def boss_rhythm():
    print("\n== BOSS RHYTHM (global action gap solved so the no-dodge damage budget = %.1fx team HP pool) ==" % BUDGET_RATIO_IF_NO_DODGE)
    for tier, (lvl, quan, hp_pct, red) in BOSS_TIERS.items():
        key = tier
        ttk = TTK[key]
        hp_char = 288_945 * (1 + 0.01 * (lvl - 1)) + 200_000 * quan
        pool = 3 * hp_char * (1 + hp_pct)
        atk = BOSS_ATK_PCT[tier] * MAX_ATK_TOTAL
        lo, hi = 0.3, 20.0
        for _ in range(40):
            gap = (lo + hi) / 2
            av, un, _ = simulate_mix(gap)
            dmg = (av + un) * atk * (1 - red) * ttk
            if dmg / pool > BUDGET_RATIO_IF_NO_DODGE:
                lo = gap
            else:
                hi = gap
        gap = round((lo + hi) / 2, 1)
        av, un, counts = simulate_mix(gap)
        print("  %-10s TTK %3ds team HP pool %s boss ATK/hit %s -> global action gap ~%.1f s; per 600 s: %s" % (
            tier, ttk, num(pool), num(atk), gap, counts))
        print("             unavoidable share of damage: %.0f%%" % (100 * un / (av + un)))


def main():
    d = json.load(open(SEED, encoding="utf-8"))
    T = {t["skillId"]: t for t in d["SkillTemplate"]}
    per = {}
    for r in d["SkillLevel"]:
        per[r["skillId"]] = per.get(r["skillId"], 0) + r["goldCost"]
    race = {}
    for sid, v in per.items():
        race[T[sid]["race"]] = race.get(T[sid]["race"], 0) + v
    common = race.get("COMMON", 0)
    skills_3 = sum(race[r] + common for r in ("EARTH", "NAMEC", "XAYDA"))
    income_total = sum(WINS_PER_LEVEL * gm(L) for L in range(1, 100))
    print("== ECONOMY ==")
    print("gold per win Gm(R)=%d x R" % GOLD_PER_WIN_PER_LEVEL)
    for R in (10, 20, 30, 50, 70, 100):
        print("  R=%3d: win %s, loss ~%s" % (R, num(gm(R)), num(gm(R) * LOSS_GOLD_FACTOR)))
    print("cumulative income 1->100 (%d wins/level): %s" % (WINS_PER_LEVEL, num(income_total)))
    print("skills, all 3 chars to max: %s (%.0f%% of income)" % (num(skills_3), 100.0 * skills_3 / income_total))
    L = 1
    while sum(WINS_PER_LEVEL * gm(i) for i in range(1, L + 1)) < skills_3:
        L += 1
    print("  level where cumulative income equals that cost (if ALL gold went to skills): ~%d" % L)
    print("potara: %d shards x %s = %s gold; cap %d/day -> min %d days; = %.0f wins at R=50" % (
        POTARA_SHARDS, num(POTARA_SHARD_PRICE), num(POTARA_SHARDS * POTARA_SHARD_PRICE), POTARA_DAILY_CAP,
        -(-POTARA_SHARDS // POTARA_DAILY_CAP), POTARA_SHARDS * POTARA_SHARD_PRICE / gm(50)))
    print("shop at recommended level R:")
    for R in (20, 50, 80):
        full = ITEM_BUY_WINS * gm(R)
        print("  R=%2d: item shard %s (item=%d shards %s), sell %s, upgrade stone %s, stat stone %s, timed item %s, revive %s / %s" % (
            R, num(full / ITEM_SHARDS_PER_ITEM), ITEM_SHARDS_PER_ITEM, num(full), num(full * SELL_FRACTION),
            num(UPGRADE_STONE_WINS * gm(R)), num(STAT_STONE_WINS * gm(R)), num(TIMED_ITEM_WINS * gm(R)),
            num(REVIVE_WINS[0] * gm(R)), num(REVIVE_WINS[1] * gm(R))))

    print("\n== COMBAT REFERENCE ==")
    print("Max_ATK_Total = %s (kept)" % num(MAX_ATK_TOTAL))
    print("team DPS per ATK point = %.2f (punch %.1f x uptime %.1f x crit %.1f x AI %.1f)" % (
        TEAM_DPS_PER_ATK, PUNCH_ATK_PER_SEC, UPTIME, CRIT_MULT, AI_EFFICIENCY))
    dps_max = TEAM_DPS_PER_ATK * MAX_ATK_TOTAL
    print("Team_DPS_Max = %s damage/s" % num(dps_max))
    rows = [("Mob thường", "early", "mob"), ("Mob thường", "mid", "mob"), ("Mob thường", "late", "mob"),
            ("Mob elite", "early", "elite"), ("Mob elite", "mid", "elite"), ("Mob elite", "late", "elite"),
            ("Boss vùng sớm", "early", "boss_early"), ("Boss vùng giữa", "mid", "boss_mid"),
            ("Boss vùng cuối", "late", "boss_late"),
            ("Mini-boss phó bản (tầng giữa)", "mid", "miniboss"), ("Boss cuối phó bản (tầng cuối)", "late", "dungeon_final"),
            ("Raid boss", "endgame", "raid")]
    print("HP = TTK x tierATK x Team_DPS_Max x (1 - target armor reduction)")
    print("%-34s %-8s %6s %6s %14s" % ("target", "tier", "TTK s", "armor", "HP"))
    for name, tier, key in rows:
        hp = TTK[key] * ATK_FRACTION[tier] * dps_max * (1.0 - TARGET_ARMOR[key])
        print("%-34s %-8s %6s %5.1f%% %14s" % (name, tier, TTK[key], 100 * TARGET_ARMOR[key], num(hp)))
    print("\n== MAX_ATK_TOTAL CHECK (U = %.0f%%) ==" % (100 * UPGRADE_MAX_PCT))
    per = (26_000 + 20_000 * (1 + UPGRADE_MAX_PCT)) * (1 + 0.25 + 0.5 + 1.0)
    print("per character %s, team %s (design constant %s)" % (num(per), num(3 * per), num(MAX_ATK_TOTAL)))

    print("\n== PLAYER ARMOR (reduction = A / (A + K), K = %s) ==" % num(ARMOR_K))
    for L in (1, 50, 100):
        a = 22_000 / 1.99 * (1 + 0.01 * (L - 1)) + 2_500
        print("  level %3d: armor %s -> %.1f%% reduction (base gear)" % (L, num(a), 100 * a / (a + ARMOR_K)))
    a_max = (22_000 + 2_500) * (1 + 0.25 + 0.5 + 1.0)
    print("  level 100 fully geared (+175%%): armor %s -> %.1f%% reduction" % (num(a_max), 100 * a_max / (a_max + ARMOR_K)))
    print("\n== DAMAGE EXAMPLE ==")
    atk = 26_000 + 20_000
    print("  lvl-100 char ATK %s, Kamejoko L7 450%%, vs boss_mid 34%% reduction:" % num(atk))
    print("    normal %s, crit (+100%%) %s" % (num(atk * 4.5 * (1 - 0.34)), num(atk * 4.5 * 2.0 * (1 - 0.34))))
    base_red = (22_000 + 2_500) / (22_000 + 2_500 + ARMOR_K)
    print("  late boss ATK 57.5%% x Max_ATK_Total = %s per hit before armor; vs lvl-100 base gear (%.1f%%): %s" % (
        num(0.575 * MAX_ATK_TOTAL), 100 * base_red, num(0.575 * MAX_ATK_TOTAL * (1 - base_red))))

    boss_rhythm()
    print("\n== FUSION PARITY (level 100, same gear) ==")
    A = 1.0
    team3_atk, team3_hp = 3 * A, 3 * A
    fus_atk, fus_hp = 2 * A + 1 * A, 2 * A + 1 * A
    print("3-char team: casters 3, total ATK %.0f, total HP %.0f (3 bodies); fusion team: casters 2 (fusion + 1 AI), total ATK %.0f, total HP %.0f (2 bodies)" % (
        team3_atk, team3_hp, fus_atk, fus_hp))


if __name__ == "__main__":
    main()
