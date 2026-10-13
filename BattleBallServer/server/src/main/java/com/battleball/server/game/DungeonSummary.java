package com.battleball.server.game;

import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;

/**
 * What a dungeon holds, for the list of the NPC Cui (docs 15.38, owner request 2026-10-13: the dungeons must say which bosses and small monsters appear and what drops): the names
 * of its bosses, its final boss, its small monsters, and the things that can drop from the monsters it has (the drop rules of its tier and of the dungeon itself).
 */
public final class DungeonSummary {
    public record Info(List<String> bosses, String finalBoss, List<String> minions, List<String> drops) {
    }

    private DungeonSummary() {
    }

    public static Info of(StaticData d, StaticData.Dungeon dungeon) {
        Set<String> bosses = new LinkedHashSet<>();
        Set<String> minions = new LinkedHashSet<>();
        String finalBoss = "";
        Set<String> classes = new LinkedHashSet<>();            // the classes of monster (BOSS, MINI, FINAL, MOB, ELITE) that this dungeon has
        for (StaticData.DungeonBoss x : dungeon.bosses().stream().sorted(java.util.Comparator.comparingInt(StaticData.DungeonBoss::sortOrder)).toList()) {
            StaticData.Boss b = d.bosses().get(x.bossId());
            if (b == null || !b.enabled()) {
                continue;
            }
            switch (x.role()) {
                case "MINION" -> {
                    minions.add(b.name());
                    classes.add(x.mobTier() == null ? "MOB" : x.mobTier());
                }
                case "FINAL" -> {
                    finalBoss = b.name();
                    classes.add("FINAL");
                }
                case "MINI" -> {
                    bosses.add(b.name());
                    classes.add("MINI");
                }
                default -> {
                    bosses.add(b.name());
                    classes.add("BOSS");
                }
            }
        }
        // the drops, in the order of the material ids (pieces, stones, book materials, earring shards); the pieces of one set and the book materials are one line each
        Map<Integer, Double> best = new TreeMap<>();
        for (StaticData.DropRule r : d.dropRules()) {
            if (!classes.contains(r.sourceClass()) || !(r.tier().equals("ANY") || r.tier().equals(dungeon.tier())) || (r.dungeonId() != null && r.dungeonId() != dungeon.id())) {
                continue;
            }
            best.merge(r.materialId(), d.dropMult(dungeon.id(), r.materialId()), Math::max);
        }
        Map<String, Double> lines = new LinkedHashMap<>();
        for (Map.Entry<Integer, Double> e : best.entrySet()) {
            StaticData.Material m = d.materials().get(e.getKey());
            if (m == null) {
                continue;
            }
            String label = switch (m.kind()) {
                case "FRAGMENT" -> {
                    String[] w = m.name().split(" ");
                    yield w.length >= 4 ? "Mảnh đồ " + String.join(" ", java.util.Arrays.copyOfRange(w, 2, w.length)) : m.name();     // "Mảnh Áo Hủy Diệt" -> "Mảnh đồ Hủy Diệt"
                }
                case "BOOK_MATERIAL" -> "Nguyên liệu sách tuyệt kỹ";
                default -> m.name();
            };
            lines.merge(label, e.getValue(), Math::max);
        }
        List<String> drops = new ArrayList<>();
        lines.forEach((label, mult) -> {
            double m = mult;
            drops.add(m > 1.0001 ? label + " (x" + (m == Math.rint(m) ? String.valueOf((long) m) : String.valueOf(m)) + ")" : label);
        });
        return new Info(List.copyOf(bosses), finalBoss, List.copyOf(minions), List.copyOf(drops));
    }
}
