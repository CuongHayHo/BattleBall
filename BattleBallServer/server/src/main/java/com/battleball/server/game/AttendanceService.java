package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.AttendanceDay;
import com.battleball.proto.AttendanceInfo;
import com.battleball.proto.RewardItem;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;

import java.sql.Connection;
import java.sql.Date;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.Supplier;

/**
 * The daily check-in of the village elder (docs/architecture/BattleBallServer.md 15.25): once a day an account gets gold and gems, and on some days of the month free items.
 * The rewards are data (balance_config "attendance", V044). The day changes at 00:00 in Vietnam (UTC+7). A day that was missed cannot be claimed later.
 * One claim a day for the whole account, however many characters it has (the wallet and the bag are shared, V026).
 */
public final class AttendanceService {
    private final Database db;
    private final StaticDataStore data;
    private final Supplier<LocalDate> today;

    public AttendanceService(Database db, StaticDataStore data) {
        this(db, data, PlayerRepository::today);
    }

    /** {@code today} is the clock: tests move it. */
    public AttendanceService(Database db, StaticDataStore data, Supplier<LocalDate> today) {
        this.db = db;
        this.data = data;
        this.today = today;
    }

    /** What one day of the month gives, for a character of this level. */
    private record Reward(long gold, long gems, List<Map.Entry<Integer, Long>> items) {
    }

    @SuppressWarnings("unchecked")
    private static Reward rewardOf(StaticData d, int day, int level) {
        Map<String, Object> cfg = d.balanceObj("attendance");
        double goldWins = Json.num(cfg, "gold_wins", 0.25);
        long gems = (long) Json.num(cfg, "gems", 3);
        List<Map.Entry<Integer, Long>> items = new ArrayList<>();
        if (cfg.get("days") instanceof List<?> days) {
            for (Object o : days) {
                if (!(o instanceof Map<?, ?>)) {
                    continue;
                }
                Map<String, Object> m = (Map<String, Object>) o;
                if ((int) Json.num(m, "day", 0) != day) {
                    continue;
                }
                goldWins += Json.num(m, "gold_wins", 0);
                gems += (long) Json.num(m, "gems", 0);
                if (m.get("items") instanceof List<?> list) {
                    for (Object io : list) {
                        if (io instanceof Map<?, ?>) {
                            Map<String, Object> im = (Map<String, Object>) io;
                            long qty = (long) Json.num(im, "qty", 0);
                            if (qty > 0) {
                                items.add(Map.entry((int) Json.num(im, "material", 0), qty));
                            }
                        }
                    }
                }
            }
        }
        return new Reward(Math.round(goldWins * EconomyService.winGold(d, level)), gems, items);
    }

    private static int topLevel(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT MAX(level) FROM game_character WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Math.max(1, rs.getInt(1)) : 1;
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

    /** The days of this month the account has already claimed. */
    private static Set<Integer> claimedDays(Connection c, long accountId, LocalDate month) throws SQLException {
        Set<Integer> out = new HashSet<>();
        try (PreparedStatement ps = c.prepareStatement("SELECT DAY(claim_date) FROM attendance_claim WHERE account_id = ? AND claim_date BETWEEN ? AND ?")) {
            ps.setLong(1, accountId);
            ps.setDate(2, Date.valueOf(month.withDayOfMonth(1)));
            ps.setDate(3, Date.valueOf(month.withDayOfMonth(month.lengthOfMonth())));
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    out.add(rs.getInt(1));
                }
            }
        }
        return out;
    }

    /** The table of the month: every day with its reward and whether it was claimed, and which day is today. */
    public AttendanceInfo info(long accountId) throws SQLException {
        StaticData d = data.get();
        LocalDate now = today.get();
        try (Connection c = db.connection()) {
            return buildInfo(c, d, accountId, now);
        }
    }

    private static AttendanceInfo buildInfo(Connection c, StaticData d, long accountId, LocalDate now) throws SQLException {
        int level = topLevel(c, accountId);
        Set<Integer> claimed = claimedDays(c, accountId, now);
        AttendanceInfo.Builder b = AttendanceInfo.newBuilder().setYear(now.getYear()).setMonth(now.getMonthValue()).setToday(now.getDayOfMonth())
                .setDaysInMonth(now.lengthOfMonth()).setClaimedToday(claimed.contains(now.getDayOfMonth())).setClaimedCount(claimed.size());
        for (int day = 1; day <= now.lengthOfMonth(); day++) {
            Reward r = rewardOf(d, day, level);
            AttendanceDay.Builder day1 = AttendanceDay.newBuilder().setDay(day).setGold(r.gold()).setGems(r.gems()).setClaimed(claimed.contains(day));
            for (Map.Entry<Integer, Long> it : r.items()) {
                StaticData.Material m = d.materials().get(it.getKey());
                day1.addItems(RewardItem.newBuilder().setTemplateId(it.getKey()).setName(m == null ? "#" + it.getKey() : m.name()).setIconId(m == null ? 0 : m.iconId())
                        .setQuantity(it.getValue()));
            }
            b.addDays(day1);
        }
        return b.build();
    }

    /** Claims today: the gold, the gems and the items of the day go to the account. Refused when today was already claimed. */
    public ActionResult claim(long accountId) throws SQLException {
        StaticData d = data.get();
        LocalDate now = today.get();
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                long character = topCharacter(c, accountId);
                if (character == 0) {
                    c.rollback();
                    return fail(ActionError.NOT_FOUND, "no character");
                }
                long goldBefore, gemsBefore;
                try (PreparedStatement ps = c.prepareStatement("SELECT gold, gems FROM account_wallet WHERE account_id = ? FOR UPDATE")) {      // the lock also keeps two claims of one account apart
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
                if (claimedDays(c, accountId, now).contains(now.getDayOfMonth())) {
                    c.rollback();
                    return fail(ActionError.ALREADY_OWNED, "already checked in today");
                }
                try (PreparedStatement ps = c.prepareStatement("INSERT INTO attendance_claim (account_id, claim_date) VALUES (?,?)")) {
                    ps.setLong(1, accountId);
                    ps.setDate(2, Date.valueOf(now));
                    ps.executeUpdate();
                }
                Reward r = rewardOf(d, now.getDayOfMonth(), topLevel(c, accountId));
                try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET gold = gold + ?, gems = gems + ?, version = version + 1 WHERE account_id = ?")) {
                    ps.setLong(1, r.gold());
                    ps.setLong(2, r.gems());
                    ps.setLong(3, accountId);
                    ps.executeUpdate();
                }
                String ref = now.toString();
                if (r.gold() > 0) {
                    EconomyService.ledger(c, character, "GOLD", r.gold(), goldBefore, goldBefore + r.gold(), "ATTENDANCE", ref);
                }
                if (r.gems() > 0) {
                    EconomyService.ledger(c, character, "GEM", r.gems(), gemsBefore, gemsBefore + r.gems(), "ATTENDANCE", ref);
                }
                for (Map.Entry<Integer, Long> it : r.items()) {
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

    private static ActionResult fail(ActionError e, String message) {
        return ActionResult.newBuilder().setOk(false).setError(e).setMessage(message).build();
    }
}
