package com.battleball.server.game.match;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * One difficulty of a dungeon run (balance_config dungeon_difficulty, docs 15.33, owner request 2026-10-11: every dungeon can be run at easy, hard or hell). The numbers are
 * multipliers over the monster table (monster_stat_class x boss_template): the HP and the damage of every monster and boss, and the gold, EXP and drop chance of the reward.
 */
public record Difficulty(String key, String name, double hpMult, double atkMult, double rewardMult, double dropMult, String note) {
    /** The unscaled numbers: a match without a difficulty (a training session, or the tests of the engine) uses these. */
    public static final Difficulty NONE = new Difficulty("", "", 1, 1, 1, 1, "");

    /** Every difficulty of the configuration, easiest first. */
    public static List<Difficulty> all(StaticData d) {
        List<Difficulty> out = new ArrayList<>();
        if (d.balanceObj("dungeon_difficulty").get("levels") instanceof List<?> levels) {
            for (Object o : levels) {
                if (o instanceof Map<?, ?> raw) {
                    @SuppressWarnings("unchecked")
                    Map<String, Object> m = (Map<String, Object>) raw;
                    out.add(new Difficulty(String.valueOf(m.get("key")), String.valueOf(m.get("name")), Json.num(m, "hp_mult", 1), Json.num(m, "atk_mult", 1),
                            Json.num(m, "reward_mult", 1), Json.num(m, "drop_mult", 1), m.get("note") instanceof String s ? s : ""));
                }
            }
        }
        return out;
    }

    /** The difficulty with this key; an empty key is the default one (the first when the configuration names none). Null when there is no such key. */
    public static Difficulty find(StaticData d, String key) {
        List<Difficulty> all = all(d);
        if (all.isEmpty()) {
            return NONE;                                       // not configured: nothing is scaled
        }
        String want = key == null || key.isBlank() ? String.valueOf(d.balanceObj("dungeon_difficulty").getOrDefault("default", all.get(0).key())) : key;
        for (Difficulty x : all) {
            if (x.key().equalsIgnoreCase(want)) {
                return x;
            }
        }
        return null;
    }
}
