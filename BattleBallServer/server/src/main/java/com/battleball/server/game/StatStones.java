package com.battleball.server.game;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The stat stone ("Ép sao") and the crystal stone ("Pha lê hóa") of Bà Hạt Mít (docs/architecture/BattleBallServer.md 15.27). A piece carries up to three stat lines (a % of the
 * character's total stat, at most 25%, the critical rate 5%) and one crystal line (red, at most 50%). The rules are data: balance_config "stat_stone_rules" (V041).
 *
 * <p>Lines are numbered: 1 to 3 are the stat lines, {@link #CRYSTAL_LINE} (4) is the crystal line.
 */
public final class StatStones {
    private StatStones() {
    }

    public static final int CRYSTAL_LINE = 4;
    public static final String STAT_SOURCE = "STAT_STONE", CRYSTAL_SOURCE = "CRYSTAL_STONE";

    public record Type(String key, double cap, double stepMin, double stepMax) {
    }

    /** What a new piece starts with at a tier: stat lines, and the most steps a line may start with. */
    public record NewLines(int lines, int maxSteps) {
    }

    public record Rules(int maxLines, List<Type> types, List<double[]> success, int addStones, int raiseStones, int retypeStones,
                        List<String> crystalTypes, double crystalStart, double crystalCap, double crystalStepMin, double crystalStepMax, int crystalStones,
                        int newGenLines, Map<Integer, NewLines> newByTier) {
        public Type type(String key) {
            for (Type t : types) {
                if (t.key().equals(key)) {
                    return t;
                }
            }
            return null;
        }

        /** The chance (percent) that one stat stone raises a line that holds {@code value} of its {@code cap}. */
        public double successPct(double value, double cap) {
            if (value + 1e-9 >= cap) {
                return 0;
            }
            double share = cap <= 0 ? 1 : value / cap;
            for (double[] row : success) {
                if (share < row[0]) {
                    return row[1];
                }
            }
            return success.isEmpty() ? 100 : success.get(success.size() - 1)[1];
        }
    }

    @SuppressWarnings("unchecked")
    public static Rules rules(StaticData d) {
        Map<String, Object> r = d.balanceObj("stat_stone_rules");
        List<Type> types = new ArrayList<>();
        if (r.get("types") instanceof List<?> list) {
            for (Object o : list) {
                if (o instanceof Map<?, ?> m) {
                    Map<String, Object> t = (Map<String, Object>) m;
                    if (t.get("key") instanceof String key) {
                        types.add(new Type(key, Json.num(t, "cap", 25), Json.num(t, "step_min", 1.5), Json.num(t, "step_max", 2.0)));
                    }
                }
            }
        }
        List<double[]> success = new ArrayList<>();
        if (r.get("success") instanceof List<?> list) {
            for (Object o : list) {
                if (o instanceof List<?> row && row.size() >= 2 && row.get(0) instanceof Number a && row.get(1) instanceof Number b) {
                    success.add(new double[]{a.doubleValue(), b.doubleValue()});
                }
            }
        }
        Map<String, Object> c = Json.obj(r, "crystal");
        List<String> crystalTypes = new ArrayList<>();
        if (c.get("types") instanceof List<?> list) {
            for (Object o : list) {
                if (o instanceof String s) {
                    crystalTypes.add(s);
                }
            }
        }
        Map<String, Object> fresh = Json.obj(r, "new_item");
        Map<Integer, NewLines> byTier = new java.util.HashMap<>();
        Map<String, Object> tiers = Json.obj(fresh, "tiers");
        for (Map.Entry<String, Object> e : tiers.entrySet()) {
            if (e.getValue() instanceof Map<?, ?> m) {
                Map<String, Object> t = (Map<String, Object>) m;
                try {
                    byTier.put(Integer.parseInt(e.getKey()), new NewLines((int) Json.num(t, "lines", 0), (int) Json.num(t, "max_steps", 1)));
                } catch (NumberFormatException ignored) {
                    // a key that is not a tier number is not a rule
                }
            }
        }
        return new Rules((int) Json.num(r, "max_lines", 3), types, success, (int) Json.num(r, "add_stones", 1), (int) Json.num(r, "raise_stones", 1),
                (int) Json.num(r, "retype_stones", 1), crystalTypes, Json.num(c, "start", 10), Json.num(c, "cap", 50), Json.num(c, "step_min", 2.0),
                Json.num(c, "step_max", 3.0), (int) Json.num(c, "stones", 1), (int) Json.num(fresh, "gen_lines", 0), byTier);
    }

    /** A value rounded to one decimal (the lines move in tenths of a percent). */
    public static double round1(double v) {
        return Math.round(v * 10.0) / 10.0;
    }

    /** The cap of a line: the crystal line has its own, a stat line the cap of its type. */
    public static double cap(Rules r, PlayerRepository.LineRow line) {
        if (CRYSTAL_SOURCE.equals(line.source())) {
            return r.crystalCap();
        }
        Type t = r.type(line.key());
        return t == null ? 0 : t.cap();
    }
}
