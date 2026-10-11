package com.battleball.server.game;

import com.battleball.server.data.StaticData;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.List;
import java.util.random.RandomGenerator;

/**
 * What a new piece of gear starts with (docs/architecture/BattleBallServer.md 15.28, owner request 2026-10-11): a GEN set in its first GEN slot and stat lines that grow with its set
 * (balance_config stat_stone_rules.new_item). A piece that is assembled, bought or sent as a gift gets them when it is made; it never starts with the red (crystal) line.
 */
final class ItemRolls {
    private ItemRolls() {
    }

    static void rollNew(Connection c, StaticData d, RandomGenerator random, long itemId, StaticData.ItemTemplate t) throws SQLException {
        StatStones.Rules rules = StatStones.rules(d);
        GenSets.Rules gen = GenSets.rules(d);
        List<GenSets.GenSet> sets = new ArrayList<>(GenSets.forRace(d, t.race()));
        for (int line = 0; line < Math.min(rules.newGenLines(), gen.initialSlots()) && !sets.isEmpty(); line++) {
            GenSets.GenSet g = sets.remove(random.nextInt(sets.size()));
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,?,?)")) {
                ps.setLong(1, itemId);
                ps.setInt(2, line);
                ps.setString(3, g.key());
                ps.executeUpdate();
            }
        }
        StatStones.NewLines fresh = rules.newByTier().get(t.tier());
        if (fresh == null || fresh.lines() <= 0) {
            return;
        }
        List<StatStones.Type> types = new ArrayList<>();
        for (StatStones.Type ty : rules.types()) {
            if (d.statEnabled(ty.key())) {
                types.add(ty);
            }
        }
        for (int no = 1; no <= Math.min(fresh.lines(), rules.maxLines()) && !types.isEmpty(); no++) {
            StatStones.Type ty = types.remove(random.nextInt(types.size()));             // never the same type twice on a piece
            int steps = Math.min(fresh.maxSteps(), 1 + (int) (random.nextDouble() * Math.max(1, fresh.maxSteps())));
            double one = Math.max(0.1, StatStones.round1(ty.stepMin() + random.nextDouble() * (ty.stepMax() - ty.stepMin())));
            double value = Math.min(ty.cap(), StatStones.round1(steps * one));
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_stat_line (item_id, line_no, stat_key, `value`, source) VALUES (?,?,?,?,?)")) {
                ps.setLong(1, itemId);
                ps.setInt(2, no);
                ps.setString(3, ty.key());
                ps.setDouble(4, value);
                ps.setString(5, StatStones.STAT_SOURCE);
                ps.executeUpdate();
            }
        }
    }
}
