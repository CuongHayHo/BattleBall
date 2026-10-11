package com.battleball.server.game.match;

import com.battleball.proto.CharacterReward;
import com.battleball.proto.DropResult;
import com.battleball.proto.MatchEnded;
import com.battleball.proto.MatchResult;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.db.Database;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.random.RandomGenerator;

/**
 * Pays a finished match in one transaction: gold, EXP and level-ups, drops, gems (first clear, timed clear), achievements, training armor points,
 * the match record and the anomaly log. The flag match_run.reward_applied makes a second call a no-op (nothing is paid twice).
 * Rules: docs/database/BattleBall.md 4.1, 10.28 to 10.31, 10.33.
 */
public final class MatchSettlement {
    private static final Logger LOG = LoggerFactory.getLogger(MatchSettlement.class);

    private final Database db;

    public MatchSettlement(Database db) {
        this.db = db;
    }

    public MatchEnded settle(MatchStarter.Started s, MatchOutcome out, RandomGenerator rng) throws SQLException {
        StaticData d = s.data();
        StaticData.Dungeon dungeon = s.setup().dungeon();
        MatchSetup.SquadMember mainMember = s.setup().squad().get(0);
        boolean won = out.result() == MatchResult.WON;
        MatchEnded.Builder end = MatchEnded.newBuilder().setRunId(s.runId()).setResult(out.result()).setBossesDefeated(out.bossesDefeated())
                .setBossCount(out.bossCount()).setDurationS(out.durationS());
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                if (!claimRun(c, s.runId(), out)) {
                    c.rollback();
                    return end.build();     // already settled
                }
                long mainId = mainMember.characterId();
                double rewardMult = s.setup().difficulty().rewardMult();                // docs 15.33: a harder run pays more
                long gold = Math.round(out.goldEarned() * rewardMult);
                if (!won) {
                    gold = Math.round(gold * d.balanceNum("gold_loss_factor", 0.5));
                }
                long walletGold = lockWalletGold(c, s.accountId());
                long spent = Math.min(out.reviveGoldSpent(), walletGold);
                long goldNet = gold;
                if (goldNet > 0) {
                    adjustWallet(c, s.accountId(), mainId, "gold", goldNet, "MATCH_REWARD", "run:" + s.runId());
                }
                if (spent > 0) {
                    adjustWallet(c, s.accountId(), mainId, "gold", -spent, "REVIVE", "run:" + s.runId());
                }
                end.setGold(Math.max(0, gold)).setReviveGoldSpent((int) spent);

                // ---- EXP and levels (the three characters, a fusion partner included; each gets the whole amount, CONFIRMED)
                double winBonus = won ? 1 + Json.num(d.balanceObj("exp_rules"), "win_bonus", 0.2) : 1.0;
                Map<Long, int[]> levelsAfter = new LinkedHashMap<>();
                for (Map.Entry<Long, Double> e : out.expRaw().entrySet()) {
                    int[] lv = applyExp(c, d, e.getKey(), Math.round(e.getValue() * winBonus * rewardMult));
                    levelsAfter.put(e.getKey(), lv);
                    end.addCharacters(CharacterReward.newBuilder().setCharacterId(e.getKey()).setExpGained(Math.round(e.getValue() * winBonus * rewardMult))
                            .setLevelBefore(lv[0]).setLevelAfter(lv[1]));
                    try (PreparedStatement ps = c.prepareStatement("INSERT INTO match_run_character (run_id, character_id, exp_gained, level_before, level_after) VALUES (?,?,?,?,?) "
                            + "ON DUPLICATE KEY UPDATE exp_gained = VALUES(exp_gained), level_after = VALUES(level_after)")) {
                        ps.setLong(1, s.runId());
                        ps.setLong(2, e.getKey());
                        ps.setLong(3, Math.round(e.getValue() * winBonus * rewardMult));
                        ps.setInt(4, lv[0]);
                        ps.setInt(5, lv[1]);
                        ps.executeUpdate();
                    }
                }

                // ---- boss rows
                int order = 0;
                for (MatchOutcome.Kill k : out.kills()) {
                    if (k.realBoss()) {
                        order++;
                        try (PreparedStatement ps = c.prepareStatement("UPDATE match_run_boss SET defeated_at = NOW() WHERE run_id = ? AND order_no = ?")) {
                            ps.setLong(1, s.runId());
                            ps.setInt(2, order);
                            ps.executeUpdate();
                        }
                    }
                }

                // ---- drops
                Map<Integer, Integer> drops = rollDrops(d, dungeon, out, rng, s.setup().difficulty().dropMult());
                for (Map.Entry<Integer, Integer> e : drops.entrySet()) {
                    StaticData.Material m = d.materials().get(e.getKey());
                    boolean toAccount = m != null && m.kind().equals("POTARA_SHARD");
                    int qty = e.getValue();
                    if (toAccount) {
                        qty = grantPotaraShards(c, s.accountId(), d, qty);
                        if (qty <= 0) {
                            continue;
                        }
                    } else {
                        com.battleball.server.game.EconomyService.addMaterial(c, s.accountId(), e.getKey(), qty);
                    }
                    com.battleball.server.game.EconomyService.itemLog(c, mainId, "DROP", toAccount ? "MATERIAL" : "MATERIAL", e.getKey(), null, qty, s.runId());
                    end.addDrops(DropResult.newBuilder().setTemplateId(e.getKey()).setQuantity(qty).setToAccount(toAccount));
                }

                // ---- gems: first clear and timed clear (the controlled character)
                int gems = 0;
                if (won && out.finalBossDefeated()) {
                    gems += firstClearGems(c, d, dungeon, mainId, end);
                    gems += timedClearGems(c, d, dungeon, mainId, out.durationS(), end);
                }
                if (gems > 0) {
                    adjustWallet(c, s.accountId(), mainId, "gems", gems, "MATCH_GEMS", "run:" + s.runId());
                }
                end.setGems(gems);

                // ---- achievements
                long wins = won ? countWins(c, s.accountId()) : 0;
                for (int id : achievements(c, d, s, out, levelsAfter, won, wins)) {
                    end.addAchievements(id);
                }

                trainArmor(c, d, s, mainMember);
                anomalies(c, d, s, out, won);
                setRunTotals(c, s.runId(), gold, gems);
                c.commit();
            } catch (SQLException | RuntimeException e) {
                c.rollback();
                throw e;
            }
        }
        return end.build();
    }

    /** Marks the run finished; false when it was already paid. */
    private boolean claimRun(Connection c, long runId, MatchOutcome out) throws SQLException {
        String status = switch (out.result()) {
            case WON -> "WON";
            case LOST -> "LOST";
            case EXPIRED -> "EXPIRED";
            default -> "ABANDONED";
        };
        try (PreparedStatement ps = c.prepareStatement("UPDATE match_run SET status = ?, ended_at = NOW(), duration_s = ?, revives_used = ?, reward_applied = 1 "
                + "WHERE run_id = ? AND reward_applied = 0")) {
            ps.setString(1, status);
            ps.setInt(2, out.durationS());
            ps.setInt(3, out.revivesUsed());
            ps.setLong(4, runId);
            return ps.executeUpdate() == 1;
        }
    }

    private void setRunTotals(Connection c, long runId, long gold, int gems) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("UPDATE match_run SET gold_earned = ?, gems_earned = ? WHERE run_id = ?")) {
            ps.setLong(1, Math.max(0, gold));
            ps.setInt(2, gems);
            ps.setLong(3, runId);
            ps.executeUpdate();
        }
    }

    private static long lockWalletGold(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT gold FROM account_wallet WHERE account_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getLong(1);
            }
        }
    }

    /** The wallet is the account's (shared by the three characters); characterId is only the character the ledger line is written for. */
    private static void adjustWallet(Connection c, long accountId, long characterId, String column, long delta, String reason, String ref) throws SQLException {
        long before;
        try (PreparedStatement ps = c.prepareStatement("SELECT " + column + " FROM account_wallet WHERE account_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                before = rs.getLong(1);
            }
        }
        long after = Math.max(0, before + delta);
        try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET " + column + " = ?, version = version + 1 WHERE account_id = ?")) {
            ps.setLong(1, after);
            ps.setLong(2, accountId);
            ps.executeUpdate();
        }
        com.battleball.server.game.EconomyService.ledger(c, characterId, column.equals("gold") ? "GOLD" : "GEM", after - before, before, after, reason, ref);
    }

    /** Adds EXP, applies level-ups (100 x L^2 to leave level L), returns {level before, level after}. At level 100 the extra EXP is dropped. */
    private int[] applyExp(Connection c, StaticData d, long characterId, long gained) throws SQLException {
        int level;
        long exp;
        try (PreparedStatement ps = c.prepareStatement("SELECT level, exp FROM game_character WHERE character_id = ? FOR UPDATE")) {
            ps.setLong(1, characterId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                level = rs.getInt(1);
                exp = rs.getLong(2);
            }
        }
        int before = level;
        int max = (int) Json.num(d.balanceObj("char_stats_lv100"), "max_level", 100);
        Map<String, Object> rules = d.balanceObj("exp_rules");
        exp += gained;
        while (level < max) {
            long need = Math.round(Json.num(rules, "coef", 100) * Math.pow(level, Json.num(rules, "power", 2)));
            if (exp < need) {
                break;
            }
            exp -= need;
            level++;
        }
        if (level >= max) {
            exp = 0;
        }
        try (PreparedStatement ps = c.prepareStatement("UPDATE game_character SET level = ?, exp = ? WHERE character_id = ?")) {
            ps.setInt(1, level);
            ps.setLong(2, exp);
            ps.setLong(3, characterId);
            ps.executeUpdate();
        }
        return new int[]{before, level};
    }

    /** Drop rules (docs/database/BattleBall.md 10.28 to 10.30): per kill, the rule of its class and tier, rate x dungeon multiplier (at most 100%). */
    static Map<Integer, Integer> rollDrops(StaticData d, StaticData.Dungeon dungeon, MatchOutcome out, RandomGenerator rng) {
        return rollDrops(d, dungeon, out, rng, 1.0);
    }

    static Map<Integer, Integer> rollDrops(StaticData d, StaticData.Dungeon dungeon, MatchOutcome out, RandomGenerator rng, double dropMult) {
        Map<Integer, Integer> result = new LinkedHashMap<>();
        for (MatchOutcome.Kill k : out.kills()) {
            for (StaticData.DropRule r : d.dropRules()) {
                if (!r.sourceClass().equals(k.classKey()) || !(r.tier().equals("ANY") || r.tier().equals(dungeon.tier()))
                        || (r.dungeonId() != null && r.dungeonId() != dungeon.id())) {
                    continue;
                }
                double rate = Math.min(100.0, r.ratePct() * d.dropMult(dungeon.id(), r.materialId()) * dropMult);
                if (rng.nextDouble() * 100.0 < rate) {
                    int qty = r.qtyMin() + (r.qtyMax() > r.qtyMin() ? (int) (rng.nextDouble() * (r.qtyMax() - r.qtyMin() + 1)) : 0);
                    result.merge(r.materialId(), Math.min(qty, r.qtyMax()), Integer::sum);
                }
            }
        }
        return result;
    }

    private int grantPotaraShards(Connection c, long accountId, StaticData d, int qty) throws SQLException {
        int needed = (int) Json.num(d.balanceObj("potara"), "shards", 100);
        try (PreparedStatement ps = c.prepareStatement("SELECT potara_owned, potara_fragments FROM account WHERE account_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                if (rs.getBoolean(1)) {
                    return 0;       // no more shards once the earring exists (CONFIRMED)
                }
                qty = Math.min(qty, needed - rs.getInt(2));
            }
        }
        if (qty > 0) {
            try (PreparedStatement ps = c.prepareStatement("UPDATE account SET potara_fragments = potara_fragments + ? WHERE account_id = ?")) {
                ps.setInt(1, qty);
                ps.setLong(2, accountId);
                ps.executeUpdate();
            }
        }
        return qty;
    }

    private int firstClearGems(Connection c, StaticData d, StaticData.Dungeon dungeon, long characterId, MatchEnded.Builder end) throws SQLException {
        StaticData.GemRule rule = gemRule(d, "FIRST_CLEAR", dungeon.id());
        if (rule == null) {
            return 0;
        }
        try (PreparedStatement ps = c.prepareStatement("INSERT IGNORE INTO character_first_clear (character_id, dungeon_id) VALUES (?,?)")) {
            ps.setLong(1, characterId);
            ps.setInt(2, dungeon.id());
            if (ps.executeUpdate() == 1) {
                end.setFirstClear(true);
                return rule.gems();
            }
        }
        return 0;
    }

    private int timedClearGems(Connection c, StaticData d, StaticData.Dungeon dungeon, long characterId, int durationS, MatchEnded.Builder end) throws SQLException {
        StaticData.GemRule rule = gemRule(d, "TIMED_CLEAR", dungeon.id());
        if (rule == null || dungeon.timeLimitS() == null || durationS > dungeon.timeLimitS()) {
            return 0;
        }
        int done = 0;
        try (PreparedStatement ps = c.prepareStatement("SELECT cnt FROM character_daily_counter WHERE character_id = ? AND counter_key = 'TIMED_CLEAR' AND day_key = ? FOR UPDATE")) {
            ps.setLong(1, characterId);
            ps.setObject(2, com.battleball.server.game.PlayerRepository.today());
            try (ResultSet rs = ps.executeQuery()) {
                if (rs.next()) {
                    done = rs.getInt(1);
                }
            }
        }
        if (rule.dailyCap() != null && done >= rule.dailyCap()) {
            return 0;
        }
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_daily_counter (character_id, counter_key, day_key, cnt) VALUES (?, 'TIMED_CLEAR', ?, 1) "
                + "ON DUPLICATE KEY UPDATE cnt = cnt + 1")) {
            ps.setLong(1, characterId);
            ps.setObject(2, com.battleball.server.game.PlayerRepository.today());
            ps.executeUpdate();
        }
        end.setTimedClear(true);
        return rule.gems();
    }

    /** The rule of a dungeon wins over the global one (dungeon_id null). */
    private static StaticData.GemRule gemRule(StaticData d, String type, int dungeonId) {
        StaticData.GemRule global = null;
        for (StaticData.GemRule r : d.gemRules()) {
            if (r.type().equals(type) && r.enabled()) {
                if (r.dungeonId() != null && r.dungeonId() == dungeonId) {
                    return r;
                }
                if (r.dungeonId() == null) {
                    global = r;
                }
            }
        }
        return global;
    }

    private long countWins(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT COUNT(*) FROM match_run WHERE account_id = ? AND status = 'WON'")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getLong(1);
            }
        }
    }

    private List<Integer> achievements(Connection c, StaticData d, MatchStarter.Started s, MatchOutcome out, Map<Long, int[]> levelsAfter, boolean won, long wins) throws SQLException {
        List<Integer> done = new ArrayList<>();
        for (StaticData.Achievement a : d.achievements()) {
            for (Map.Entry<Long, int[]> e : levelsAfter.entrySet()) {
                long characterId = e.getKey();
                String race = raceOf(c, characterId);
                if (!a.race().equals("ANY") && !a.race().equals(race)) {
                    continue;
                }
                boolean reached = switch (a.condition()) {
                    case "LEVEL_REACH" -> e.getValue()[1] >= a.threshold();
                    case "WINS_TOTAL" -> won && characterId == s.setup().squad().get(0).characterId() && wins >= a.threshold();
                    case "FIRST_CLEAR_BOSS" -> out.kills().stream().anyMatch(k -> k.realBoss() && k.bossId() == a.threshold());
                    default -> false;
                };
                if (!reached) {
                    continue;
                }
                try (PreparedStatement ps = c.prepareStatement("INSERT IGNORE INTO character_achievement (character_id, achievement_id) VALUES (?,?)")) {
                    ps.setLong(1, characterId);
                    ps.setInt(2, a.id());
                    if (ps.executeUpdate() == 0) {
                        continue;
                    }
                }
                if (a.rewardCostumeId() != null) {
                    try (PreparedStatement ps = c.prepareStatement("INSERT IGNORE INTO owned_skin (account_id, costume_id, source) VALUES (?,?,'REWARD')")) {
                        ps.setLong(1, s.accountId());
                        ps.setInt(2, a.rewardCostumeId());
                        ps.executeUpdate();
                    }
                }
                if (a.rewardGems() != null && a.rewardGems() > 0) {
                    adjustWallet(c, s.accountId(), characterId, "gems", a.rewardGems(), "ACHIEVEMENT", "achievement:" + a.id());
                }
                if (!done.contains(a.id())) {
                    done.add(a.id());
                }
            }
        }
        return done;
    }

    private static String raceOf(Connection c, long characterId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT race FROM game_character WHERE character_id = ?")) {
            ps.setLong(1, characterId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getString(1);
            }
        }
    }

    /** Training armor (docs 10.21): worn = it gains points each match; not worn = one stored point is used up for the +ATK that match. */
    private void trainArmor(Connection c, StaticData d, MatchStarter.Started s, MatchSetup.SquadMember main) throws SQLException {
        List<Long> ids = new ArrayList<>();
        for (MatchSetup.SquadMember m : s.setup().squad()) {
            ids.add(m.characterId());
            if (m.partnerCharacterId() != 0) {
                ids.add(m.partnerCharacterId());
            }
        }
        for (long id : ids) {
            Long worn = null;
            try (PreparedStatement ps = c.prepareStatement("SELECT support_instance_id FROM character_equipment WHERE character_id = ? AND slot = 'SUPPORT_2'")) {
                ps.setLong(1, id);
                try (ResultSet rs = ps.executeQuery()) {
                    if (rs.next() && rs.getObject(1) != null) {
                        worn = rs.getLong(1);
                    }
                }
            }
            if (worn != null) {
                int support = 0;
                try (PreparedStatement ps = c.prepareStatement("SELECT support_id FROM character_support_item WHERE instance_id = ?")) {
                    ps.setLong(1, worn);
                    try (ResultSet rs = ps.executeQuery()) {
                        if (rs.next()) {
                            support = rs.getInt(1);
                        }
                    }
                }
                StaticData.SupportItem def = d.supportItems().get(support);
                if (def != null && def.train() != null) {
                    try (PreparedStatement ps = c.prepareStatement("UPDATE character_support_item SET charge_points = LEAST(?, charge_points + ?) WHERE instance_id = ?")) {
                        ps.setInt(1, def.train().maxPoints());
                        ps.setInt(2, def.train().pointsPerMatch());
                        ps.setLong(3, worn);
                        ps.executeUpdate();
                    }
                }
            } else {
                // not worn: the instance with the biggest bonus that still has points gives its bonus this match and loses one point
                try (PreparedStatement ps = c.prepareStatement("SELECT instance_id, support_id FROM character_support_item WHERE account_id = ? AND charge_points > 0 "
                        + "AND instance_id NOT IN (SELECT support_instance_id FROM character_equipment WHERE support_instance_id IS NOT NULL)")) {
                    ps.setLong(1, s.accountId());
                    long best = 0;
                    double bestBonus = 0;
                    try (ResultSet rs = ps.executeQuery()) {
                        while (rs.next()) {
                            StaticData.SupportItem def = d.supportItems().get(rs.getInt(2));
                            if (def != null && def.train() != null && def.train().atkBonusPct() > bestBonus) {
                                bestBonus = def.train().atkBonusPct();
                                best = rs.getLong(1);
                            }
                        }
                    }
                    if (best != 0) {
                        try (PreparedStatement up = c.prepareStatement("UPDATE character_support_item SET charge_points = charge_points - 1 WHERE instance_id = ? AND charge_points > 0")) {
                            up.setLong(1, best);
                            up.executeUpdate();
                        }
                    }
                }
            }
        }
    }

    /** Log only, never blocks (CONFIRMED anti-cheat decision): a very short win, or a boss killed far faster than designed. */
    private void anomalies(Connection c, StaticData d, MatchStarter.Started s, MatchOutcome out, boolean won) throws SQLException {
        if (won && out.durationS() < 15) {
            anomaly(c, s, "SHORT_MATCH", "{\"duration_s\":" + out.durationS() + "}");
        }
        if (out.fastestBossKillS() > 0) {
            double design = 1e9;
            for (MatchOutcome.Kill k : out.kills()) {
                if (k.realBoss()) {
                    StaticData.MonsterStat ms = d.monsterStat(k.classKey(), s.setup().dungeon().tier());
                    if (ms != null) {
                        design = Math.min(design, ms.ttkSeconds());
                    }
                }
            }
            if (out.fastestBossKillS() < design / 10.0) {
                anomaly(c, s, "FAST_BOSS", "{\"fastest_kill_s\":" + out.fastestBossKillS() + ",\"design_ttk_s\":" + design + "}");
            }
        }
    }

    private void anomaly(Connection c, MatchStarter.Started s, String kind, String detailJson) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO anomaly_log (account_id, run_id, kind, detail) VALUES (?,?,?,?)")) {
            ps.setLong(1, s.accountId());
            ps.setLong(2, s.runId());
            ps.setString(3, kind);
            ps.setString(4, detailJson);
            ps.executeUpdate();
        }
        LOG.warn("anomaly {} in run {}: {}", kind, s.runId(), detailJson);
    }
}
