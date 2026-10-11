"""
Balance of the stat stone ("Ep sao") and the crystal stone ("Pha le hoa"), docs/architecture/BattleBallServer.md 15.27 and 15.28.

It reads nothing from the database: the numbers below mirror balance_config.stat_stone_rules, item_economy_wins, gold_per_win_per_level and the drop_rule rows of V016/V041.
Run:  python balance_stat_stones.py

What it answers (Monte Carlo, the same rules as EconomyService.statStone):
  * how many stat stones one line needs from its first step to its maximum, for a table of success chances;
  * what a whole build costs (5 pieces of a character: 3 stat lines and the red line each) and how many won matches that is,
    given that a stone is sold for one won match of gold (item_economy_wins.stat_stone = 1.0) and some drop;
  * how many matches the drops alone would need.
"""
import random
import statistics

random.seed(7)

CURRENT = [(0.5, 100), (0.7, 80), (0.85, 60), (0.95, 40), (1.01, 20)]          # V041
GENTLER = [(0.6, 100), (0.8, 85), (0.9, 65), (0.97, 45), (1.01, 30)]           # V042 (the table in use)

LINES = {  # cap, step_min, step_max, first value (one step: the average)
    'stat line (ATK, HP, ARMOR, KI, CRIT_DMG)': (25, 1.5, 2.0, 1.75),
    'critical rate line': (5, 0.1, 0.5, 0.3),
    'red (crystal) line': (50, 2.0, 3.0, 10.0),
}

# drops per match of a late dungeon (V016): 4 bosses + a final boss (one mini-boss counted too), stat stone #202
DROPS_STAT_STONE = {'BOSS': 1.3, 'MINI': 2.0, 'FINAL': 4.0}
DROPS_CRYSTAL = {'FINAL': 0.4}
MATCH_KILLS = {'BOSS': 4, 'MINI': 1, 'FINAL': 1}


def chance(share, table):
    for upto, pct in table:
        if share < upto:
            return pct
    return table[-1][1]


def stones_to_max(cap, smin, smax, start, table, runs=4000):
    out = []
    for _ in range(runs):
        v, n = start, 0
        while v + 1e-9 < cap:
            n += 1
            if random.random() * 100 < chance(v / cap, table):
                v = min(cap, round(v + random.uniform(smin, smax), 1))
        out.append(n)
    return statistics.mean(out), statistics.quantiles(out, n=20)[18]


def report(name, table):
    print('== success table', name)
    means = {}
    for label, (cap, lo, hi, start) in LINES.items():
        mean, p95 = stones_to_max(cap, lo, hi, start, table)
        means[label] = mean
        print('   %-42s average %5.1f stones, 1 time in 20 more than %4.0f' % (label, mean, p95))
    stat, red = means['stat line (ATK, HP, ARMOR, KI, CRIT_DMG)'], means['red (crystal) line']
    per_piece = 3 * stat + red
    per_character = 5 * per_piece
    print('   one piece (3 stat lines + red): %.0f stones; the 5 pieces of a character: %.0f; the three characters: %.0f' % (per_piece, per_character, 3 * per_character))
    drop = sum(MATCH_KILLS[k] * DROPS_STAT_STONE.get(k, 0) / 100 for k in MATCH_KILLS)
    print('   stat stones that drop in a match of a late dungeon: %.3f (so about %.0f matches of drops for one character); bought at 1 won match of gold each: about %.0f won matches' %
          (drop, per_character / drop, per_character))
    crystals = 5 * 1
    cdrop = sum(MATCH_KILLS[k] * DROPS_CRYSTAL.get(k, 0) / 100 for k in MATCH_KILLS)
    print('   crystal stones for the 5 pieces of a character: %d; 500 gems each = %d gems (about %d days of 50 gems); by drops about %.0f matches' %
          (crystals, crystals * 500, crystals * 500 / 50, crystals / cdrop))


if __name__ == '__main__':
    report('V041 (first proposal)', CURRENT)
    report('V042 (in use)', GENTLER)
