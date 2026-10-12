package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.MailAttachment;
import com.battleball.proto.MailInfo;
import com.battleball.proto.MailList;
import com.battleball.proto.NoticeInfo;
import com.battleball.proto.NoticeList;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The mailbox (gifts from the admin) and the notices. A mail belongs to one account; claiming pays every attachment in one transaction
 * (gold and gems into the shared wallet, skins, materials and gear into the shared bag, the Potara earring and shards onto the account)
 * and sets claimed_at, so a gift cannot be taken twice. Blocking: call from the DB executor. Tables: V027__mailbox_and_notices.sql.
 */
public final class MailService {
    static final int LIST_LIMIT = 100;
    static final int NOTICE_LIMIT = 50;

    private final Database db;
    private final StaticDataStore data;

    public MailService(Database db, StaticDataStore data) {
        this.db = db;
        this.data = data;
    }

    // ---- reading ----------------------------------------------------------------------------------------------------------

    /** Mails not read yet or with a gift not taken yet (the badge on the menu). */
    public static int unread(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT COUNT(*) FROM mail m WHERE m.account_id = ? AND (m.read_at IS NULL OR (m.claimed_at IS NULL "
                + "AND (m.expires_at IS NULL OR m.expires_at > NOW()) AND EXISTS (SELECT 1 FROM mail_attachment a WHERE a.mail_id = m.mail_id)))")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        }
    }

    public MailList list(long accountId) throws SQLException {
        StaticData d = data.get();
        try (Connection c = db.connection()) {
            Map<Long, MailInfo.Builder> mails = new LinkedHashMap<>();
            try (PreparedStatement ps = c.prepareStatement("SELECT mail_id, title, body, UNIX_TIMESTAMP(sent_at) * 1000, read_at IS NOT NULL, claimed_at IS NOT NULL, "
                    + "COALESCE(UNIX_TIMESTAMP(expires_at) * 1000, 0), expires_at IS NOT NULL AND expires_at <= NOW() "
                    + "FROM mail WHERE account_id = ? ORDER BY mail_id DESC LIMIT " + LIST_LIMIT)) {
                ps.setLong(1, accountId);
                try (ResultSet rs = ps.executeQuery()) {
                    while (rs.next()) {
                        mails.put(rs.getLong(1), MailInfo.newBuilder().setMailId(rs.getLong(1)).setTitle(rs.getString(2)).setBody(rs.getString(3))
                                .setSentAtMs(rs.getLong(4)).setRead(rs.getBoolean(5)).setClaimed(rs.getBoolean(6))
                                .setExpiresAtMs(rs.getLong(7)).setExpired(rs.getBoolean(8) && !rs.getBoolean(6)));
                    }
                }
            }
            Map<String, Integer> icons = new HashMap<>();
            try (PreparedStatement ps = c.prepareStatement("SELECT a.mail_id, a.kind, a.ref_id, a.quantity FROM mail_attachment a JOIN mail m USING (mail_id) "
                    + "WHERE m.account_id = ? ORDER BY a.mail_id, a.line_no")) {
                ps.setLong(1, accountId);
                try (ResultSet rs = ps.executeQuery()) {
                    while (rs.next()) {
                        MailInfo.Builder m = mails.get(rs.getLong(1));
                        if (m != null) {
                            m.addAttachments(attachment(c, d, rs.getString(2), rs.getInt(3), rs.getLong(4), icons));
                        }
                    }
                }
            }
            MailList.Builder out = MailList.newBuilder();
            for (MailInfo.Builder m : mails.values()) {
                if (m.getAttachmentsCount() == 0) {
                    m.setClaimed(true);         // nothing to take
                }
                out.addMails(m);
            }
            return out.build();
        }
    }

    private static MailAttachment attachment(Connection c, StaticData d, String kind, int ref, long qty, Map<String, Integer> icons) throws SQLException {
        MailAttachment.Builder b = MailAttachment.newBuilder().setKind(kind).setRefId(ref).setQuantity(qty);
        switch (kind) {
            case "GOLD" -> b.setName("Vàng");
            case "GEMS" -> b.setName("Đá quý");
            case "SKIN" -> {
                StaticData.Skin s = d.skins().get(ref);
                b.setName("Cải trang " + (s == null ? "#" + ref : s.name()));
            }
            case "MATERIAL" -> {
                StaticData.Material m = d.materials().get(ref);
                b.setName(m == null ? "#" + ref : m.name()).setIconId(icon(c, "material_template", ref, icons));
            }
            case "ITEM" -> {
                StaticData.ItemTemplate t = d.items().get(ref);
                b.setName(t == null ? "#" + ref : t.name()).setIconId(icon(c, "item_template", ref, icons));
            }
            case "EARRING" -> b.setName("Bông tai Porata").setIconId((int) Json.num(d.balanceObj("potara"), "icon_id", 15614));
            default -> b.setName(kind);
        }
        return b.build();
    }

    private static int icon(Connection c, String table, int templateId, Map<String, Integer> cache) throws SQLException {
        String key = table + ":" + templateId;
        Integer known = cache.get(key);
        if (known != null) {
            return known;
        }
        int icon = 0;
        try (PreparedStatement ps = c.prepareStatement("SELECT icon_id FROM " + table + " WHERE template_id = ?")) {      // the table name is one of two constants above
            ps.setInt(1, templateId);
            try (ResultSet rs = ps.executeQuery()) {
                if (rs.next()) {
                    icon = rs.getInt(1);
                }
            }
        }
        cache.put(key, icon);
        return icon;
    }

    public NoticeList notices() throws SQLException {
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement("SELECT notice_id, title, body, UNIX_TIMESTAMP(created_at) * 1000 FROM notice WHERE enabled = 1 "
                     + "AND (expires_at IS NULL OR expires_at > NOW()) ORDER BY notice_id DESC LIMIT " + NOTICE_LIMIT);
             ResultSet rs = ps.executeQuery()) {
            NoticeList.Builder out = NoticeList.newBuilder();
            while (rs.next()) {
                out.addNotices(NoticeInfo.newBuilder().setNoticeId(rs.getLong(1)).setTitle(rs.getString(2)).setBody(rs.getString(3)).setCreatedAtMs(rs.getLong(4)));
            }
            return out.build();
        }
    }

    // ---- actions ------------------------------------------------------------------------------------------------------------

    public ActionResult read(long accountId, long mailId) {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement("UPDATE mail SET read_at = COALESCE(read_at, NOW()) WHERE mail_id = ? AND account_id = ?")) {
            ps.setLong(1, mailId);
            ps.setLong(2, accountId);
            return ps.executeUpdate() == 0 ? fail(ActionError.NOT_FOUND, "mail not found") : ok();
        } catch (SQLException e) {
            return fail(ActionError.ACTION_INTERNAL, "database error");
        }
    }

    public ActionResult delete(long accountId, long mailId) {
        try (Connection c = db.connection()) {
            // a gift that was not taken keeps the mail (the player must not lose it by a mistaken tap)
            try (PreparedStatement ps = c.prepareStatement("DELETE FROM mail WHERE mail_id = ? AND account_id = ? AND (claimed_at IS NOT NULL OR (expires_at IS NOT NULL AND expires_at <= NOW()) "
                    + "OR NOT EXISTS (SELECT 1 FROM mail_attachment a WHERE a.mail_id = mail.mail_id))")) {
                ps.setLong(1, mailId);
                ps.setLong(2, accountId);
                if (ps.executeUpdate() > 0) {
                    return ok();
                }
            }
            try (PreparedStatement ps = c.prepareStatement("SELECT 1 FROM mail WHERE mail_id = ? AND account_id = ?")) {
                ps.setLong(1, mailId);
                ps.setLong(2, accountId);
                try (ResultSet rs = ps.executeQuery()) {
                    return rs.next() ? fail(ActionError.DISABLED, "take the gift first") : fail(ActionError.NOT_FOUND, "mail not found");
                }
            }
        } catch (SQLException e) {
            return fail(ActionError.ACTION_INTERNAL, "database error");
        }
    }

    /** Takes the gift of one mail: every attachment or nothing. */
    public ActionResult claim(long accountId, long mailId) {
        StaticData d = data.get();
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                ActionResult r = claimIn(c, d, accountId, mailId);
                if (r.getOk()) {
                    c.commit();
                } else {
                    c.rollback();
                }
                return r;
            } catch (SQLException | RuntimeException e) {
                c.rollback();
                throw e;
            }
        } catch (SQLException e) {
            return fail(ActionError.ACTION_INTERNAL, "database error");
        }
    }

    /** Takes the gift of every mail that still has one, newest last; stops at the first mail that cannot be paid (bag full). */
    public ActionResult claimAll(long accountId) {
        List<Long> ids = new ArrayList<>();
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement("SELECT m.mail_id FROM mail m WHERE m.account_id = ? AND m.claimed_at IS NULL AND (m.expires_at IS NULL OR m.expires_at > NOW()) "
                     + "AND EXISTS (SELECT 1 FROM mail_attachment a WHERE a.mail_id = m.mail_id) ORDER BY m.mail_id")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    ids.add(rs.getLong(1));
                }
            }
        } catch (SQLException e) {
            return fail(ActionError.ACTION_INTERNAL, "database error");
        }
        if (ids.isEmpty()) {
            return fail(ActionError.NOT_FOUND, "no gift to take");
        }
        int taken = 0;
        ActionResult last = null;
        for (long id : ids) {
            last = claim(accountId, id);
            if (!last.getOk()) {
                break;
            }
            taken++;
        }
        return taken > 0 ? ActionResult.newBuilder().setOk(true).setSuccess(true).setNewId(taken).build() : last;
    }

    private ActionResult claimIn(Connection c, StaticData d, long accountId, long mailId) throws SQLException {
        boolean claimed, expired;
        try (PreparedStatement ps = c.prepareStatement("SELECT claimed_at IS NOT NULL, expires_at IS NOT NULL AND expires_at <= NOW() FROM mail WHERE mail_id = ? AND account_id = ? FOR UPDATE")) {
            ps.setLong(1, mailId);
            ps.setLong(2, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    return fail(ActionError.NOT_FOUND, "mail not found");
                }
                claimed = rs.getBoolean(1);
                expired = rs.getBoolean(2);
            }
        }
        if (claimed) {
            return fail(ActionError.ALREADY_OWNED, "the gift was already taken");
        }
        if (expired) {
            return fail(ActionError.DISABLED, "the gift has expired");
        }
        record Line(String kind, int ref, long qty) {
        }
        List<Line> lines = new ArrayList<>();
        try (PreparedStatement ps = c.prepareStatement("SELECT kind, ref_id, quantity FROM mail_attachment WHERE mail_id = ? ORDER BY line_no")) {
            ps.setLong(1, mailId);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    lines.add(new Line(rs.getString(1), rs.getInt(2), rs.getLong(3)));
                }
            }
        }
        if (lines.isEmpty()) {
            return fail(ActionError.NOT_FOUND, "this mail has no gift");
        }
        long anchor = anchorCharacter(c, accountId);
        if (anchor == 0) {
            return fail(ActionError.NOT_FOUND, "account has no character");
        }
        // the shared wallet is locked first (the same order as the economy actions)
        long gold;
        long gems;
        try (PreparedStatement ps = c.prepareStatement("SELECT gold, gems FROM account_wallet WHERE account_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    return fail(ActionError.NOT_FOUND, "no wallet");
                }
                gold = rs.getLong(1);
                gems = rs.getLong(2);
            }
        }
        long pieces = lines.stream().filter(l -> l.kind().equals("ITEM")).mapToLong(Line::qty).sum();
        if (pieces > 0 && bagCount(c, accountId) + pieces > bagLimit(d)) {
            return fail(ActionError.BAG_FULL, "the bag is full");
        }
        String ref = "mail:" + mailId;
        for (Line l : lines) {
            switch (l.kind()) {
                case "GOLD" -> {
                    pay(c, accountId, "gold", l.qty());
                    EconomyService.ledger(c, anchor, "GOLD", l.qty(), gold, gold + l.qty(), "MAIL", ref);
                    gold += l.qty();
                }
                case "GEMS" -> {
                    pay(c, accountId, "gems", l.qty());
                    EconomyService.ledger(c, anchor, "GEM", l.qty(), gems, gems + l.qty(), "MAIL", ref);
                    gems += l.qty();
                }
                case "SKIN" -> {
                    if (!d.skins().containsKey(l.ref())) {
                        return fail(ActionError.NOT_FOUND, "the skin of this gift does not exist");
                    }
                    try (PreparedStatement ps = c.prepareStatement("INSERT IGNORE INTO owned_skin (account_id, costume_id, source) VALUES (?,?,'ADMIN')")) {
                        ps.setLong(1, accountId);
                        ps.setInt(2, l.ref());
                        ps.executeUpdate();
                    }
                    EconomyService.itemLog(c, anchor, "REWARD", "SKIN", l.ref(), null, 1, null);
                }
                case "MATERIAL" -> {
                    StaticData.Material m = d.materials().get(l.ref());
                    if (m == null) {
                        return fail(ActionError.NOT_FOUND, "the material of this gift does not exist");
                    }
                    if (m.kind().equals("POTARA_SHARD")) {
                        grantShards(c, d, accountId, l.qty());          // shards live on the account, capped at 100, none once the earring exists
                    } else {
                        EconomyService.addMaterial(c, accountId, l.ref(), l.qty());
                    }
                    EconomyService.itemLog(c, anchor, "REWARD", "MATERIAL", l.ref(), null, (int) Math.min(l.qty(), Integer.MAX_VALUE), null);
                }
                case "ITEM" -> {
                    StaticData.ItemTemplate t = d.items().get(l.ref());
                    if (t == null) {
                        return fail(ActionError.NOT_FOUND, "the item of this gift does not exist");
                    }
                    for (long i = 0; i < l.qty(); i++) {
                        long itemId;
                        try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_instance (account_id, template_id, slot) VALUES (?,?,?)", Statement.RETURN_GENERATED_KEYS)) {
                            ps.setLong(1, accountId);
                            ps.setInt(2, l.ref());
                            ps.setString(3, t.slot());
                            ps.executeUpdate();
                            try (ResultSet rs = ps.getGeneratedKeys()) {
                                rs.next();
                                itemId = rs.getLong(1);
                            }
                        }
                        ItemRolls.rollNew(c, d, java.util.concurrent.ThreadLocalRandom.current(), itemId, t);
                        EconomyService.itemLog(c, anchor, "REWARD", "ITEM", l.ref(), itemId, 1, null);
                    }
                }
                case "EARRING" -> {
                    try (PreparedStatement ps = c.prepareStatement("UPDATE account SET potara_owned = 1 WHERE account_id = ?")) {
                        ps.setLong(1, accountId);
                        ps.executeUpdate();
                    }
                }
                default -> {
                    return fail(ActionError.INVALID_ARGUMENT, "unknown gift kind " + l.kind());
                }
            }
        }
        try (PreparedStatement ps = c.prepareStatement("UPDATE mail SET claimed_at = NOW(), read_at = COALESCE(read_at, NOW()) WHERE mail_id = ?")) {
            ps.setLong(1, mailId);
            ps.executeUpdate();
        }
        return ok();
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------------

    private static void pay(Connection c, long accountId, String column, long amount) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET " + column + " = " + column + " + ?, version = version + 1 WHERE account_id = ?")) {
            ps.setLong(1, amount);
            ps.setLong(2, accountId);
            ps.executeUpdate();
        }
    }

    private static void grantShards(Connection c, StaticData d, long accountId, long qty) throws SQLException {
        int needed = (int) Json.num(d.balanceObj("potara"), "shards", 100);
        try (PreparedStatement ps = c.prepareStatement("SELECT potara_owned, potara_fragments FROM account WHERE account_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                if (rs.getBoolean(1)) {
                    return;
                }
                qty = Math.min(qty, needed - rs.getInt(2));
            }
        }
        if (qty > 0) {
            try (PreparedStatement ps = c.prepareStatement("UPDATE account SET potara_fragments = potara_fragments + ? WHERE account_id = ?")) {
                ps.setLong(1, qty);
                ps.setLong(2, accountId);
                ps.executeUpdate();
            }
        }
    }

    private static long anchorCharacter(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT MIN(character_id) FROM game_character WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
            }
        }
    }

    private static int bagCount(Connection c, long accountId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT COUNT(*) FROM item_instance WHERE account_id = ? AND location = 'BAG'")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        }
    }

    private static int bagLimit(StaticData d) {
        return (int) Json.num(d.balanceObj("inventory_rules"), "bag_limit", 2000);
    }

    private static ActionResult fail(ActionError e, String message) {
        return ActionResult.newBuilder().setOk(false).setError(e).setMessage(message).build();
    }

    private static ActionResult ok() {
        return ActionResult.newBuilder().setOk(true).setSuccess(true).build();
    }
}
