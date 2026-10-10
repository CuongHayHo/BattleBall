package com.battleball.server.game;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * GEN: the activation sets of the original game (Teamobi SetClothes: a piece carries the tag of a set, five pieces of the same set worn together switch its effect on;
 * docs/architecture/BattleBallServer.md 15.21). Owner decisions 2026-10-10: the effect is a bonus to one skill, a piece may carry several GEN lines (only sets of its planet),
 * a stone changes a line to another random set, another stone opens one more line.
 *
 * <p>The sets and the rules are data (balance_config "gen_sets" and "gen_rules", V039). A set has tiers ("pieces" worn needed); for every kind of effect the highest tier reached
 * decides the value (Teamobi: the 4-piece and 5-piece steps replace the lower one), except that the effects of the lower tiers stay when the higher tier does not mention that kind.
 * The totals this class returns are plain numbers by key: a stat key (HP_PCT, CRIT_DMG_PCT, GOLD_PCT...) or {@code TYPE:skill} for one skill (SKILL_DMG_PCT:1 = Kamejoko +%).
 */
public final class GenSets {
    private GenSets() {
    }

    /** One effect: {@code type} is STAT (a stat key, then {@code stat} is the key) or a SKILL_* kind with the skill id. */
    public record Effect(String type, int skill, String stat, double value) {
        /** The key of the total this effect adds to. */
        public String key() {
            return type.equals("STAT") ? stat : type + ":" + skill;
        }
    }

    public record Tier(int pieces, String text, List<Effect> effects) {
    }

    public record GenSet(String key, String race, String name, List<Tier> tiers) {
        public boolean fits(String itemRace) {
            return race.equals("ALL") || race.equals(itemRace);
        }
    }

    public record Rules(int initialSlots, int maxSlots, List<Integer> slotUnlockStones, int rerollStones) {
        /** Stones the next slot costs when the piece has {@code slots} slots now; 0 when no further slot can be opened. */
        public int unlockCost(int slots) {
            if (slots >= maxSlots || slots - initialSlots >= slotUnlockStones.size()) {
                return 0;
            }
            return slotUnlockStones.get(Math.max(0, slots - initialSlots));
        }
    }

    private record Parsed(List<GenSet> sets, Rules rules) {
    }

    private static volatile StaticData cachedFor;
    private static volatile Parsed cached;

    private static Parsed parsed(StaticData d) {
        Parsed p = cached;
        if (p != null && cachedFor == d) {
            return p;
        }
        p = parse(d);
        cached = p;
        cachedFor = d;
        return p;
    }

    @SuppressWarnings("unchecked")
    private static Parsed parse(StaticData d) {
        List<GenSet> sets = new ArrayList<>();
        if (d.balanceObj("gen_sets").get("items") instanceof List<?> list) {
            for (Object o : list) {
                if (!(o instanceof Map<?, ?>)) {
                    continue;
                }
                Map<String, Object> m = (Map<String, Object>) o;
                String key = m.get("key") instanceof String s ? s : "";
                if (key.isEmpty() || Boolean.FALSE.equals(m.get("enabled"))) {
                    continue;
                }
                List<Tier> tiers = new ArrayList<>();
                if (m.get("tiers") instanceof List<?> ts) {
                    for (Object to : ts) {
                        if (!(to instanceof Map<?, ?>)) {
                            continue;
                        }
                        Map<String, Object> tm = (Map<String, Object>) to;
                        List<Effect> effects = new ArrayList<>();
                        if (tm.get("effects") instanceof List<?> es) {
                            for (Object eo : es) {
                                if (eo instanceof Map<?, ?>) {
                                    Map<String, Object> em = (Map<String, Object>) eo;
                                    effects.add(new Effect(String.valueOf(em.get("type")), (int) Json.num(em, "skill", 0),
                                            em.get("stat") instanceof String s ? s : "", Json.num(em, "value", 0)));
                                }
                            }
                        }
                        tiers.add(new Tier((int) Json.num(tm, "pieces", 5), tm.get("text") instanceof String s ? s : "", effects));
                    }
                }
                tiers.sort((a, b) -> Integer.compare(a.pieces(), b.pieces()));
                sets.add(new GenSet(key, m.get("race") instanceof String s ? s : "ALL", m.get("name") instanceof String s ? s : key, tiers));
            }
        }
        Map<String, Object> r = d.balanceObj("gen_rules");
        List<Integer> unlock = new ArrayList<>();
        if (r.get("slot_unlock_stones") instanceof List<?> us) {
            for (Object u : us) {
                unlock.add(u instanceof Number n ? n.intValue() : 1);
            }
        }
        Rules rules = new Rules(Math.max(0, (int) Json.num(r, "initial_slots", 1)), Math.max(1, (int) Json.num(r, "max_slots", 3)), unlock,
                Math.max(1, (int) Json.num(r, "reroll_stones", 1)));
        return new Parsed(sets, rules);
    }

    public static List<GenSet> all(StaticData d) {
        return parsed(d).sets();
    }

    public static GenSet find(StaticData d, String key) {
        for (GenSet s : all(d)) {
            if (s.key().equals(key)) {
                return s;
            }
        }
        return null;
    }

    public static Rules rules(StaticData d) {
        return parsed(d).rules();
    }

    /** The sets a piece of this planet may carry. */
    public static List<GenSet> forRace(StaticData d, String itemRace) {
        List<GenSet> out = new ArrayList<>();
        for (GenSet s : all(d)) {
            if (s.fits(itemRace)) {
                out.add(s);
            }
        }
        return out;
    }

    /** Worn pieces of each set: a piece counts once for a set however many lines it has of it. */
    public static Map<String, Integer> counts(Collection<? extends Collection<String>> gensOfWornPieces) {
        Map<String, Integer> out = new HashMap<>();
        for (Collection<String> gens : gensOfWornPieces) {
            for (String g : new java.util.HashSet<>(gens)) {
                out.merge(g, 1, Integer::sum);
            }
        }
        return out;
    }

    /** Totals of every effect the worn pieces switch on (see the class comment for the keys). */
    public static Map<String, Double> effects(StaticData d, Collection<? extends Collection<String>> gensOfWornPieces) {
        Map<String, Integer> counts = counts(gensOfWornPieces);
        Map<String, Double> totals = new HashMap<>();
        for (GenSet s : all(d)) {
            int n = counts.getOrDefault(s.key(), 0);
            Map<String, Double> chosen = new HashMap<>();
            for (Tier t : s.tiers()) {                       // ascending: a higher tier overwrites the same kind of effect of a lower one
                if (n >= t.pieces()) {
                    for (Effect e : t.effects()) {
                        chosen.put(e.key(), e.value());
                    }
                }
            }
            chosen.forEach((k, v) -> totals.merge(k, v, Double::sum));
        }
        return totals;
    }

    /** The tiers of a set that {@code n} worn pieces reach, for the text under the character. */
    public static List<Tier> reached(GenSet s, int n) {
        List<Tier> out = new ArrayList<>();
        for (Tier t : s.tiers()) {
            if (n >= t.pieces()) {
                out.add(t);
            }
        }
        return out;
    }
}
