package com.battleball.server.game;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.Collection;
import java.util.HashMap;
import java.util.Map;

/**
 * Final stats of one character (docs/database/BattleBall.md section 4, CONFIRMED order):
 * (stats of the level + stats of the gear) x (1 + sum of all % bonuses, added together, never compounded), then buffs and fusion afterwards.
 * Only stat types that are enabled on the admin page count. The server recomputes these whenever gear or skin changes; clients only display them.
 */
public final class StatCalculator {
    private StatCalculator() {
    }

    /** One worn piece of gear: its main stat (already multiplied by its upgrade %) and its extra stat lines. */
    public record GearPiece(String mainStatKey, double mainValue, Map<String, Double> lines) {
    }

    public record CharacterStats(long hpMax, long kiMax, long atk, long armor, double critRatePct, double critDmgPct, double dodgePct,
                                 double hpRegenPctPerSec, double kiRegenPctPerSec, Map<String, Double> other) {
        public double other(String key) {
            return other.getOrDefault(key, 0.0);
        }
    }

    /** Level-100 numbers and the 1%-per-level slope come from balance_config.char_stats_lv100. */
    public static double levelFactor(StaticData d, int level) {
        double step = Json.num(d.balanceObj("char_stats_lv100"), "level_step_pct", 1.0) / 100.0;
        return 1.0 + step * (level - 1);
    }

    public static CharacterStats compute(StaticData d, int level, Collection<GearPiece> gear, Collection<Map<String, Double>> bonusMaps) {
        Map<String, Object> c = d.balanceObj("char_stats_lv100");
        int maxLevel = (int) Json.num(c, "max_level", 100);
        double factorMax = levelFactor(d, maxLevel);
        double f = levelFactor(d, level);
        double hp = Json.num(c, "hp", 575000) / factorMax * f;
        double ki = Json.num(c, "ki", 575000) / factorMax * f;
        double atk = Json.num(c, "atk", 26000) / factorMax * f;
        double armor = Json.num(c, "armor", 22000) / factorMax * f;
        double critRate = Json.num(c, "crit_rate_pct_per_level", 0.1) * level;
        double critDmg = Json.num(c, "crit_dmg_pct_per_level", 1.0) * level;

        // everything that is not a base number goes into one table of totals, so % bonuses are summed (not compounded)
        Map<String, Double> total = new HashMap<>();
        for (GearPiece g : gear) {
            add(d, total, g.mainStatKey(), g.mainValue());
            g.lines().forEach((k, v) -> add(d, total, k, v));
        }
        for (Map<String, Double> m : bonusMaps) {
            m.forEach((k, v) -> add(d, total, k, v));
        }
        hp = (hp + total.getOrDefault("FLAT_HP", 0.0)) * (1 + total.getOrDefault("HP_PCT", 0.0) / 100.0);
        ki = (ki + total.getOrDefault("FLAT_KI", 0.0)) * (1 + total.getOrDefault("KI_PCT", 0.0) / 100.0);
        atk = (atk + total.getOrDefault("FLAT_ATK", 0.0)) * (1 + total.getOrDefault("ATK_PCT", 0.0) / 100.0);
        armor = (armor + total.getOrDefault("FLAT_ARMOR", 0.0)) * (1 + total.getOrDefault("ARMOR_PCT", 0.0) / 100.0);
        critRate = Math.min(100.0, critRate + total.getOrDefault("CRIT_RATE_PCT", 0.0));   // rate is clamped to 100% (CONFIRMED)
        critDmg = critDmg + total.getOrDefault("CRIT_DMG_PCT", 0.0);                       // damage has no cap (CONFIRMED)
        double hpRegen = d.balanceNum("hp_regen_pct_per_sec", 0.5) + total.getOrDefault("HP_REGEN_PCT", 0.0);
        double kiRegen = d.balanceNum("ki_regen_pct_per_sec", 5.0) + total.getOrDefault("KI_REGEN_PCT", 0.0);
        return new CharacterStats(Math.round(hp), Math.round(ki), Math.round(atk), Math.round(armor), critRate, critDmg,
                total.getOrDefault("DODGE_PCT", 0.0), hpRegen, kiRegen, Map.copyOf(total));
    }

    private static void add(StaticData d, Map<String, Double> total, String key, double value) {
        if (key != null && d.statEnabled(key)) {
            total.merge(key, value, Double::sum);
        }
    }

    /** Damage reduction of an armor value: A / (A + K), capped (balance_config armor_k, armor_reduction_cap). */
    public static double armorReduction(StaticData d, double armor) {
        double k = d.balanceNum("armor_k", 50_000);
        double cap = d.balanceNum("armor_reduction_cap", 0.8);
        return Math.min(cap, armor <= 0 ? 0 : armor / (armor + k));
    }
}
