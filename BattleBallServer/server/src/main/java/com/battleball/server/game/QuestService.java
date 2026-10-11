package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.QuestEntry;
import com.battleball.proto.RewardItem;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Supplier;

/**
 * The quests of Ông Gôhan (docs/architecture/BattleBallServer.md 15.32). The quests are data (balance_config "quests", V045): a chain of TUTORIAL quests done once and one at a time, and DAILY
 * quests that start again at 00:00 in Vietnam (UTC+7). The server counts events as the player acts ({@link #record}), the client only shows the list that comes inside every
 * PlayerDataSnapshot and the arrow towards the NPC of the quest. Rewards: gold (in wins of the highest character), gems and materials (the skill cards, the stones).
 * Two types are read from the account instead of counted: LEVEL (the highest character) and OWN_EARRING (the Potara earring).
 */
public final class QuestService {
    /** The skill card (material_template 206): given by quests, paid by learning or raising a skill (skill_level.card_cost). */
    public static final int SKILL_CARD = 206;

    private final Database db;
    private final StaticDataStore data;
    private final Supplier<LocalDate> today;

    public QuestService(Database db, StaticDataStore data) {
        this(db, data, PlayerRepository::today);
    }

    /** {@code today} is the clock: tests move it. */
    public QuestService(Database db, StaticDataStore data, Supplier<LocalDate> today) {
        this.db = db;
        this.data = data;
        this.today = today;
    }

    /** One quest of the table. */
    record Def(String id, boolean daily, String title, String desc, String type, int target, String npc, double goldWins, long gems, List<Map.Entry<Integer, Long>> items) {
        String period(LocalDate now) {
            return daily ? now.toString() : "0";
        }

        boolean stateBased() {
            return type.equals("LEVEL") || type.equals("OWN_EARRING");
        }
    }

    @SuppressWarnings("unchecked")
    static List<Def> defs(StaticData d) {
        List<Def> out = new ArrayList<>();
        if (!(d.balanceObj("quests").get("quests") instanceof List<?> list)) {
            return out;
        }
        for (Object o : list) {
            if (!(o instanceof Map<?, ?>)) {
                continue;
            }
            Map<String, Object> m = (Map<String, Object>) o;
            Map<String, Object> r = Json.obj(m, "reward");
            List<Map.Entry<Integer, Long>> items = new ArrayList<>();
            if (r.get("items") instanceof List<?> its) {
                for (Object io : its) {
                    if (io instanceof Map<?, ?>) {
                        Map<String, Object> im = (Map<String, Object>) io;
                        long qty = (long) Json.num(im, "qty", 0);
                        if (qty > 0) {
                            items.add(Map.entry((int) Json.num(im, "material", 0), qty));
                        }
                    }
                }
            }
            out.add(new Def(str(m, "id"), "DAILY".equals(str(m, "kind")), str(m, "title"), str(m, "desc"), str(m, "type"), Math.max(1, (int) Json.num(m, "target", 1)),
                    str(m, "npc"), Json.num(r, "gold_wins", 0), (long) Json.num(r, "gems", 0), items));
        }
        return out;
    }

    private static String str(Map<String, Object> m, String key) {
        return m.get(key) instanceof String s ? s : "";
    }

    /** What the account has done: the stored counters of the quests of today and of the chain, and which of them were claimed. */
    private record State(Map<String, Integer> progress, Map<String, Boolean> claimed, int level, boolean earring) {
        int of(Def q) {
            if (q.type().equals("LEVEL")) {
                return Math.min(q.target(), level);
            }
            if (q.type().equals("OWN_EARRING")) {
                return earring ? Math.min(q.target(), 1) : 0;
            }
            return Math.min(q.target(), progress.getOrDefault(q.id(), 0));
        }

        boolean done(Def q) {
            return of(q) >= q.target();
        }

        boolean claimed(Def q) {
            return claimed.getOrDefault(q.id(), false);
        }
    }

    private static State load(Connection c, long accountId, List<Def> defs, LocalDate now) throws SQLException {
        Map<String, Integer> progress = new HashMap<>();
        Map<String, Boolean> claimed = new HashMap<>();
        try (PreparedStatement ps = c.prepareStatement("SELECT quest_id, period_key, progress, claimed_at IS NOT NULL FROM quest_progress WHERE account_id = ? AND period_key IN ('0', ?)")) {
            ps.setLong(1, accountId);
            ps.setString(2, now.toString());
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    String id = rs.getString(1);
                    boolean daily = defs.stream().anyMatch(q -> q.id().equals(id) && q.daily());
                    if (daily == !rs.getString(2).equals("0")) {         // a daily quest only counts for today's row, a tutorial one only for its '0' row
                        progress.put(id, rs.getInt(3));
                        claimed.put(id, rs.getBoolean(4));
                    }
                }
            }
        }
        int level = 1;
        boolean earring = false;
        try (PreparedStatement ps = c.prepareStatement("SELECT COALESCE((SELECT MAX(level) FROM game_character WHERE account_id = ?), 1), potara_owned FROM account WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            ps.setLong(2, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                if (rs.next()) {
                    level = Math.max(1, rs.getInt(1));
                    earring = rs.getBoolean(2);
                }
            }
        }
        return new State(progress, claimed, level, earring);
    }

    /** The tutorial quest being done: the first one of the chain not yet claimed (null when the chain is finished). */
    private static Def activeTutorial(List<Def> defs, State s) {
        for (Def q : defs) {
            if (!q.daily() && !s.claimed(q)) {
                return q;
            }
        }
        return null;
    }

    /** The quests the player sees: the one of the chain being done, then the daily ones. */
    private static List<Def> visible(List<Def> defs, State s) {
        List<Def> out = new ArrayList<>();
        Def t = activeTutorial(defs, s);
        if (t != null) {
            out.add(t);
        }
        for (Def q : defs) {
            if (q.daily()) {
                out.add(q);
            }
        }
        return out;
    }

    // ---- counting ----------------------------------------------------------------------------------------------------------

    /** Counts an event for every quest of this type (the daily ones for today, the chain ones for good). Returns true when a counter changed. */
    public boolean record(long accountId, String type, int amount) throws SQLException {
        if (amount <= 0) {
            return false;
        }
        StaticData d = data.get();
        LocalDate now = today.get();
        boolean changed = false;
        try (Connection c = db.connection()) {
            for (Def q : defs(d)) {
                if (!q.type().equals(type) || q.stateBased()) {
                    continue;
                }
                try (PreparedStatement ps = c.prepareStatement("INSERT INTO quest_progress (account_id, quest_id, period_key, progress) VALUES (?,?,?,?) "
                        + "ON DUPLICATE KEY UPDATE progress = IF(claimed_at IS NULL, LEAST(?, progress + ?), progress)")) {
                    ps.setLong(1, accountId);
                    ps.setString(2, q.id());
                    ps.setString(3, q.period(now));
                    ps.setInt(4, Math.min(q.target(), amount));
                    ps.setInt(5, q.target());
                    ps.setInt(6, amount);
                    changed |= ps.executeUpdate() > 0;
                }
            }
        }
        return changed;
    }

    // ---- the list for the snapshot ----------------------------------------------------------------------------------------

    /** The quests the player sees now, for PlayerDataSnapshot.quests. */
    public List<QuestEntry> entries(Connection c, long accountId) throws SQLException {
        StaticData d = data.get();
        List<Def> defs = defs(d);
        if (defs.isEmpty()) {
            return List.of();
        }
        State s = load(c, accountId, defs, today.get());
        int level = s.level();
        List<QuestEntry> out = new ArrayList<>();
        for (Def q : visible(defs, s)) {
            QuestEntry.Builder b = QuestEntry.newBuilder().setId(q.id()).setKind(q.daily() ? "DAILY" : "TUTORIAL").setTitle(q.title()).setDescription(q.desc()).setType(q.type())
                    .setProgress(s.of(q)).setTarget(q.target()).setNpcId(q.npc()).setGold(Math.round(q.goldWins() * EconomyService.winGold(d, level))).setGems((int) q.gems())
                    .setClaimed(s.claimed(q));
            for (Map.Entry<Integer, Long> it : q.items()) {
                StaticData.Material m = d.materials().get(it.getKey());
                b.addItems(RewardItem.newBuilder().setTemplateId(it.getKey()).setName(m == null ? "#" + it.getKey() : m.name()).setIconId(m == null ? 0 : m.iconId()).setQuantity(it.getValue()));
            }
            out.add(b.build());
        }
        return out;
    }

    // ---- the reward --------------------------------------------------------------------------------------------------------

    /**
     * Pays the reward of a finished quest, or of every finished one when {@code questId} is empty. A tutorial quest can only be claimed when it is the one being done;
     * claiming it moves the chain on (the next one may already be finished and is paid too when everything is claimed at once).
     */
    public ActionResult claim(long accountId, String questId) throws SQLException {
        StaticData d = data.get();
        LocalDate now = today.get();
        List<Def> defs = defs(d);
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                long character = topCharacter(c, accountId);
                if (character == 0) {
                    c.rollback();
                    return fail(ActionError.NOT_FOUND, "no character");
                }
                long goldBefore, gemsBefore;
                try (PreparedStatement ps = c.prepareStatement("SELECT gold, gems FROM account_wallet WHERE account_id = ? FOR UPDATE")) {      // the lock keeps two claims of one account apart
                    ps.setLong(1, accountId);
                    try (ResultSet rs = ps.executeQuery()) {
                        if (!rs.next()) {
                            c.rollback();
                            return fail(ActionError.NOT_FOUND, "no wallet");
                        }
                        goldBefore = rs.getLong(1);
                        gemsBefore = rs.getLong(2);
                    }
                }
                State s = load(c, accountId, defs, now);
                boolean all = questId == null || questId.isEmpty();
                if (!all && defs.stream().noneMatch(q -> q.id().equals(questId))) {
                    c.rollback();
                    return fail(ActionError.NOT_FOUND, "no such quest");
                }
                long gold = 0, gems = 0, paid = 0;
                Map<Integer, Long> items = new HashMap<>();
                boolean again = true;
                while (again) {
                    again = false;
                    for (Def q : visible(defs, s)) {
                        if ((!all && !q.id().equals(questId)) || s.claimed(q) || !s.done(q)) {
                            continue;
                        }
                        gold += Math.round(q.goldWins() * EconomyService.winGold(d, s.level()));
                        gems += q.gems();
                        q.items().forEach(it -> items.merge(it.getKey(), it.getValue(), Long::sum));
                        try (PreparedStatement ps = c.prepareStatement("INSERT INTO quest_progress (account_id, quest_id, period_key, progress, claimed_at) VALUES (?,?,?,?,NOW()) "
                                + "ON DUPLICATE KEY UPDATE claimed_at = NOW()")) {
                            ps.setLong(1, accountId);
                            ps.setString(2, q.id());
                            ps.setString(3, q.period(now));
                            ps.setInt(4, s.of(q));
                            ps.executeUpdate();
                        }
                        s.claimed().put(q.id(), true);
                        paid++;
                        if (all && !q.daily()) {
                            again = true;                        // the chain moved on: the next quest may be finished already
                        }
                    }
                }
                if (paid == 0) {
                    c.rollback();
                    return fail(ActionError.INVALID_ARGUMENT, "no finished quest to claim");
                }
                try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET gold = gold + ?, gems = gems + ?, version = version + 1 WHERE account_id = ?")) {
                    ps.setLong(1, gold);
                    ps.setLong(2, gems);
                    ps.setLong(3, accountId);
                    ps.executeUpdate();
                }
                String ref = all ? "all" : questId;
                if (gold > 0) {
                    EconomyService.ledger(c, character, "GOLD", gold, goldBefore, goldBefore + gold, "QUEST", ref);
                }
                if (gems > 0) {
                    EconomyService.ledger(c, character, "GEM", gems, gemsBefore, gemsBefore + gems, "QUEST", ref);
                }
                for (Map.Entry<Integer, Long> it : items.entrySet()) {
                    EconomyService.addMaterial(c, accountId, it.getKey(), it.getValue());
                    EconomyService.itemLog(c, character, "REWARD", "MATERIAL", it.getKey(), null, (int) (long) it.getValue(), null);
                }
                c.commit();
                return ActionResult.newBuilder().setOk(true).setSuccess(true).build();
            } catch (SQLException | RuntimeException e) {
                c.rollback();
                throw e;
            }
        }
    }

    private static long topCharacter(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT character_id FROM game_character WHERE account_id = ? ORDER BY level DESC, character_id LIMIT 1")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
            }
        }
    }

    private static ActionResult fail(ActionError e, String message) {
        return ActionResult.newBuilder().setOk(false).setError(e).setMessage(message).build();
    }
}
