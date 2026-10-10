package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.InventoryPage;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;

import java.security.SecureRandom;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.random.RandomGenerator;

/**
 * Everything a player does outside a match: shop, skins, skills, gear, support items, crafting, Potara.
 * One transaction per action; the account's wallet row is locked first (the wallet is shared by the three characters), so two requests cannot spend the same gold.
 * Rules: docs/database/BattleBall.md sections 3, 10.18 to 10.25. Blocking: call from the DB executor.
 */
public final class EconomyService {
    private static final List<String> GEAR_SLOTS = List.of("ARMOR", "PANTS", "GLOVES", "BOOTS", "RING");
    private static final Map<String, String> SUPPORT_SLOT = Map.of("PET", "SUPPORT_1", "TRAIN_ARMOR", "SUPPORT_2",
            "FLAG_WING_WEAPON", "SUPPORT_3", "SKILL_BOOK", "SUPPORT_4");
    private static final int POTARA_SHARD_ID = 1820;      // Nro item 1820, "Manh vo bong tai cap 3" (V025)

    private final Database db;
    private final StaticDataStore data;
    private final RandomGenerator random;

    public EconomyService(Database db, StaticDataStore data) {
        this(db, data, new SecureRandom());
    }

    public EconomyService(Database db, StaticDataStore data, RandomGenerator random) {
        this.db = db;
        this.data = data;
        this.random = random;
    }

    // ---- plumbing ------------------------------------------------------------------------------------------------------

    /** The locked character and wallet. */
    record Ch(long id, long accountId, String race, int level, long gold, long gems) {
    }

    @FunctionalInterface
    private interface Body {
        ActionResult run(Connection c, StaticData d, Ch ch) throws SQLException;
    }

    private static ActionResult fail(ActionError e, String message) {
        return ActionResult.newBuilder().setOk(false).setError(e).setMessage(message).build();
    }

    private static ActionResult ok() {
        return ActionResult.newBuilder().setOk(true).setSuccess(true).build();
    }

    private static ActionResult ok(long newId, boolean success) {
        return ActionResult.newBuilder().setOk(true).setSuccess(success).setNewId(newId).build();
    }

    private ActionResult tx(long accountId, long characterId, Body body) {
        StaticData d = data.get();
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                Ch ch = lockCharacter(c, accountId, characterId);
                if (ch == null) {
                    c.rollback();
                    return fail(ActionError.NOT_FOUND, "character not found");
                }
                ActionResult r = body.run(c, d, ch);
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

    private static Ch lockCharacter(Connection c, long accountId, long characterId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT c.race, c.level, w.gold, w.gems FROM game_character c JOIN account_wallet w ON w.account_id = c.account_id "
                + "WHERE c.character_id = ? AND c.account_id = ? FOR UPDATE")) {
            ps.setLong(1, characterId);
            ps.setLong(2, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? new Ch(characterId, accountId, rs.getString(1), rs.getInt(2), rs.getLong(3), rs.getLong(4)) : null;
            }
        }
    }

    static int raceIndex(String race) {
        return switch (race) {
            case "EARTH" -> 0;
            case "NAMEC" -> 1;
            default -> 2;
        };
    }

    private static boolean raceFits(String raceLimit, String race) {
        return raceLimit == null || raceLimit.equals("ANY") || raceLimit.equals("COMMON") || raceLimit.equals(race);
    }

    private static boolean skinFits(StaticData.Skin skin, String race) {
        if (skin.genderId() != 3 && skin.genderId() != raceIndex(race)) {
            return false;
        }
        String f = skin.fusionRace();
        if (f == null || f.equals("ANY")) {
            return true;
        }
        return switch (f) {
            case "td" -> race.equals("EARTH");
            case "nm" -> race.equals("NAMEC");
            case "xd" -> race.equals("XAYDA");
            default -> false;
        };
    }

    static long winGold(StaticData d, int level) {
        return Math.round(d.balanceNum("gold_per_win_per_level", 1000) * Math.max(1, level));
    }

    private static void spend(Connection c, Ch ch, String currency, long amount, String reason, String ref) throws SQLException {
        long before = currency.equals("GOLD") ? ch.gold() : ch.gems();
        String col = currency.equals("GOLD") ? "gold" : "gems";
        try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET " + col + " = " + col + " - ?, version = version + 1 WHERE account_id = ?")) {
            ps.setLong(1, amount);
            ps.setLong(2, ch.accountId());
            ps.executeUpdate();
        }
        ledger(c, ch.id(), currency, -amount, before, before - amount, reason, ref);
    }

    private static void earn(Connection c, Ch ch, String currency, long amount, String reason, String ref) throws SQLException {
        long before = currency.equals("GOLD") ? ch.gold() : ch.gems();
        String col = currency.equals("GOLD") ? "gold" : "gems";
        try (PreparedStatement ps = c.prepareStatement("UPDATE account_wallet SET " + col + " = " + col + " + ?, version = version + 1 WHERE account_id = ?")) {
            ps.setLong(1, amount);
            ps.setLong(2, ch.accountId());
            ps.executeUpdate();
        }
        ledger(c, ch.id(), currency, amount, before, before + amount, reason, ref);
    }

    public static void ledger(Connection c, long characterId, String currency, long delta, long before, long after, String reason, String ref) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO currency_ledger (character_id, currency, delta, balance_before, balance_after, reason, ref) VALUES (?,?,?,?,?,?,?)")) {
            ps.setLong(1, characterId);
            ps.setString(2, currency);
            ps.setLong(3, delta);
            ps.setLong(4, before);
            ps.setLong(5, after);
            ps.setString(6, reason);
            ps.setString(7, ref);
            ps.executeUpdate();
        }
    }

    public static void itemLog(Connection c, long characterId, String action, String kind, int templateId, Long itemId, int quantity, Long runId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_log (character_id, action, template_kind, template_id, item_id, quantity, run_id) VALUES (?,?,?,?,?,?,?)")) {
            ps.setLong(1, characterId);
            ps.setString(2, action);
            ps.setString(3, kind);
            ps.setInt(4, templateId);
            ps.setObject(5, itemId);
            ps.setInt(6, quantity);
            ps.setObject(7, runId);
            ps.executeUpdate();
        }
    }

    static long materialQty(Connection c, long accountId, int templateId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT quantity FROM material_stack WHERE account_id = ? AND template_id = ? FOR UPDATE")) {
            ps.setLong(1, accountId);
            ps.setInt(2, templateId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
            }
        }
    }

    /** Adds (delta &gt; 0) or removes (delta &lt; 0) materials. The column is unsigned, so a removal is an UPDATE that refuses to go below zero. */
    public static void addMaterial(Connection c, long accountId, int templateId, long delta) throws SQLException {
        if (delta > 0) {
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO material_stack (account_id, template_id, quantity) VALUES (?,?,?) "
                    + "ON DUPLICATE KEY UPDATE quantity = quantity + VALUES(quantity)")) {
                ps.setLong(1, accountId);
                ps.setInt(2, templateId);
                ps.setLong(3, delta);
                ps.executeUpdate();
            }
        } else if (delta < 0) {
            try (PreparedStatement ps = c.prepareStatement("UPDATE material_stack SET quantity = quantity - ? WHERE account_id = ? AND template_id = ? AND quantity >= ?")) {
                ps.setLong(1, -delta);
                ps.setLong(2, accountId);
                ps.setInt(3, templateId);
                ps.setLong(4, -delta);
                if (ps.executeUpdate() == 0) {
                    throw new SQLException("not enough of material " + templateId);
                }
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

    // ---- skins and skills --------------------------------------------------------------------------------------------------

    public ActionResult buySkin(long accountId, long characterId, int costumeId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.Skin skin = d.skins().get(costumeId);
            StaticData.SkinKind kind = skin == null ? null : d.skinKinds().get(skin.kindKey());
            if (skin == null || kind == null) {
                return fail(ActionError.NOT_FOUND, "skin not found");
            }
            if (!skin.enabled() || !kind.enabled() || !kind.sellable() || skin.gemPrice() == null) {
                return fail(ActionError.DISABLED, "this skin is not for sale");
            }
            if (!skinFits(skin, ch.race())) {
                return fail(ActionError.RACE_LOCKED, "this skin is for another race");
            }
            if (exists(c, "SELECT 1 FROM owned_skin WHERE account_id = ? AND costume_id = ?", ch.accountId(), costumeId)) {
                return fail(ActionError.ALREADY_OWNED, "already owned");
            }
            if (ch.gems() < skin.gemPrice()) {
                return fail(ActionError.NOT_ENOUGH_GEMS, "not enough gems");
            }
            spend(c, ch, "GEM", skin.gemPrice(), "BUY_SKIN", "skin:" + costumeId);
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO owned_skin (account_id, costume_id, source) VALUES (?,?,'PURCHASE')")) {
                ps.setLong(1, ch.accountId());
                ps.setInt(2, costumeId);
                ps.executeUpdate();
            }
            itemLog(c, ch.id(), "BUY", "SKIN", costumeId, null, 1, null);
            return ok();
        });
    }

    public ActionResult equipSkin(long accountId, long characterId, int costumeId) {
        return equipSkin(accountId, characterId, costumeId, false);
    }

    /**
     * Wears a skin. An ordinary skin goes in the character's SKIN slot (it changes how the character looks and its own stats). A fusion skin (fusion = true)
     * goes in the FUSION_SKIN slot: it is worn by the fusion form of the main character, so only a character of the race it was made for can hold it,
     * and it counts only while that character is the main one of a fusion (owner decision 2026-10-08).
     * costume_id 0 takes the worn skin off that slot (owner request 2026-10-08, "Hỗ trợ 5").
     */
    public ActionResult equipSkin(long accountId, long characterId, int costumeId, boolean fusion) {
        return tx(accountId, characterId, (c, d, ch) -> {
            if (costumeId == 0) {                          // take off: the slot (the character's own, or the fusion one) is emptied and the race's own look shows
                try (PreparedStatement ps = c.prepareStatement("DELETE FROM character_equipment WHERE character_id = ? AND slot = ?")) {
                    ps.setLong(1, ch.id());
                    ps.setString(2, fusion ? "FUSION_SKIN" : "SKIN");
                    ps.executeUpdate();
                }
                return ok();
            }
            StaticData.Skin skin = d.skins().get(costumeId);
            StaticData.SkinKind kind = skin == null ? null : d.skinKinds().get(skin.kindKey());
            if (skin == null || kind == null) {
                return fail(ActionError.NOT_FOUND, "skin not found");
            }
            if (!exists(c, "SELECT 1 FROM owned_skin WHERE account_id = ? AND costume_id = ?", ch.accountId(), costumeId)) {
                return fail(ActionError.NOT_OWNED, "skin not owned");
            }
            if (!skin.enabled() || !kind.enabled() || !(kind.playerWearable() || kind.requiresFusionMain())) {
                return fail(ActionError.DISABLED, "this skin cannot be worn");
            }
            if (kind.requiresFusionMain() != fusion) {
                return fail(ActionError.DISABLED, fusion ? "this is not a fusion skin" : "a fusion skin is worn by the fusion form");
            }
            if (!skinFits(skin, ch.race())) {
                return fail(ActionError.RACE_LOCKED, "this skin is for another race");
            }
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_equipment (character_id, slot, costume_id) VALUES (?, ?, ?) "
                    + "ON DUPLICATE KEY UPDATE costume_id = VALUES(costume_id)")) {
                ps.setLong(1, ch.id());
                ps.setString(2, fusion ? "FUSION_SKIN" : "SKIN");
                ps.setInt(3, costumeId);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    /**
     * Throws a skin away (owner request 2026-10-08, "vứt"): the account loses it for good. A skin that one of the account's characters wears (SKIN or FUSION_SKIN)
     * must be taken off first. {@code anyCharacterId} only gives the transaction a character to lock and to write the item log under.
     */
    public ActionResult discardSkin(long accountId, long anyCharacterId, int costumeId) {
        return tx(accountId, anyCharacterId, (c, d, ch) -> {
            if (!exists(c, "SELECT 1 FROM owned_skin WHERE account_id = ? AND costume_id = ? FOR UPDATE", accountId, costumeId)) {
                return fail(ActionError.NOT_OWNED, "skin not owned");
            }
            if (exists(c, "SELECT 1 FROM character_equipment e JOIN game_character g ON g.character_id = e.character_id WHERE g.account_id = ? AND e.costume_id = ?", accountId, costumeId)) {
                return fail(ActionError.DISABLED, "take the skin off first");
            }
            try (PreparedStatement ps = c.prepareStatement("DELETE FROM owned_skin WHERE account_id = ? AND costume_id = ?")) {
                ps.setLong(1, accountId);
                ps.setInt(2, costumeId);
                ps.executeUpdate();
            }
            itemLog(c, ch.id(), "DESTROY", "SKIN", costumeId, null, 1, null);
            return ok();
        });
    }

    /**
     * Wears an aura (0 takes it off). An aura is unlocked when the highest character of the account has reached its level; one aura at a time, shared by the
     * three characters, a visual effect only (owner decision 2026-10-08). {@code anyCharacterId} only gives the transaction a character to lock.
     */
    public ActionResult equipAura(long accountId, long anyCharacterId, int auraId) {
        return tx(accountId, anyCharacterId, (c, d, ch) -> {
            if (auraId != 0) {
                Auras.Aura aura = Auras.find(d, auraId);
                if (aura == null || !aura.enabled()) {
                    return fail(ActionError.NOT_FOUND, "aura not found");
                }
                int highest = 0;
                try (PreparedStatement ps = c.prepareStatement("SELECT COALESCE(MAX(level), 0) FROM game_character WHERE account_id = ?")) {
                    ps.setLong(1, accountId);
                    try (ResultSet rs = ps.executeQuery()) {
                        if (rs.next()) {
                            highest = rs.getInt(1);
                        }
                    }
                }
                if (highest < aura.level()) {
                    return fail(ActionError.LEVEL_TOO_LOW, "the aura unlocks at level " + aura.level());
                }
            }
            try (PreparedStatement ps = c.prepareStatement("UPDATE account SET equipped_aura_id = ? WHERE account_id = ?")) {
                ps.setObject(1, auraId == 0 ? null : auraId);
                ps.setLong(2, accountId);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    public ActionResult upgradeSkill(long accountId, long characterId, int skillId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.Skill skill = d.skills().get(skillId);
            if (skill == null || !skill.enabled() || !raceFits(skill.race(), ch.race())) {
                return fail(ActionError.NOT_FOUND, "skill not available");
            }
            int current = 0;
            try (PreparedStatement ps = c.prepareStatement("SELECT `level` FROM character_skill WHERE character_id = ? AND skill_id = ? FOR UPDATE")) {
                ps.setLong(1, ch.id());
                ps.setInt(2, skillId);
                try (ResultSet rs = ps.executeQuery()) {
                    if (rs.next()) {
                        current = rs.getInt(1);
                    }
                }
            }
            if (current >= skill.maxLevel()) {
                return fail(ActionError.MAXED, "skill is at its maximum level");
            }
            StaticData.SkillLevel next = skill.level(current + 1);
            if (next == null) {
                return fail(ActionError.NOT_FOUND, "no data for the next level");
            }
            if (ch.gold() < next.goldCost()) {
                return fail(ActionError.NOT_ENOUGH_GOLD, "not enough gold");
            }
            spend(c, ch, "GOLD", next.goldCost(), "UPGRADE_SKILL", "skill:" + skillId + ":" + (current + 1));
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_skill (character_id, skill_id, `level`) VALUES (?,?,?) "
                    + "ON DUPLICATE KEY UPDATE `level` = VALUES(`level`)")) {
                ps.setLong(1, ch.id());
                ps.setInt(2, skillId);
                ps.setInt(3, current + 1);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    // ---- shop: materials, gear --------------------------------------------------------------------------------------------

    public ActionResult buyMaterial(long accountId, long characterId, int templateId, int quantity) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.Material m = d.materials().get(templateId);
            if (m == null || !m.enabled() || quantity < 1 || quantity > 1000) {
                return fail(ActionError.INVALID_ARGUMENT, "material or quantity not valid");
            }
            if (m.kind().equals("POTARA_SHARD") || m.buyCurrency().equals("NONE")) {
                return fail(ActionError.DISABLED, "this material is not sold here");
            }
            long unit;
            String currency = m.buyCurrency();
            if (currency.equals("GOLD")) {
                unit = goldUnitPrice(d, m, ch.level());
            } else {
                unit = m.priceFixed() == null ? 0 : m.priceFixed();
            }
            long total = unit * quantity;
            if (unit <= 0) {
                return fail(ActionError.DISABLED, "no price set");
            }
            if ((currency.equals("GOLD") ? ch.gold() : ch.gems()) < total) {
                return fail(currency.equals("GOLD") ? ActionError.NOT_ENOUGH_GOLD : ActionError.NOT_ENOUGH_GEMS, "not enough " + currency.toLowerCase());
            }
            spend(c, ch, currency, total, "BUY_MATERIAL", "material:" + templateId + "x" + quantity);
            addMaterial(c, ch.accountId(), templateId, quantity);
            itemLog(c, ch.id(), "BUY", "MATERIAL", templateId, null, quantity, null);
            return ok();
        });
    }

    /** The gold one unit of a material costs a character of this level (the price in wins times the gold of a win at that level), or the fixed price. */
    static long goldUnitPrice(StaticData d, StaticData.Material m, int level) {
        return m.priceFixed() != null ? m.priceFixed() : (long) Math.ceil(m.priceWins() * winGold(d, level));
    }

    /**
     * A whole piece of gear bought in a shop (Bunma): the price of all its fragments in gold, paid and assembled in one step (the plan: "mua đủ một món bằng vàng ≈ 6 trận thắng").
     * Same rules as buying the fragments and assembling them: the level, the bag room, the gold.
     */
    public ActionResult buyGear(long accountId, long characterId, int itemTemplateId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.ItemTemplate t = d.items().get(itemTemplateId);
            if (t == null || !t.enabled() || !GEAR_SLOTS.contains(t.slot())) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            if (ch.level() < t.minLevel()) {
                return fail(ActionError.LEVEL_TOO_LOW, "level too low");
            }
            int fragmentItem = d.fragmentRoot(t).id();
            StaticData.Material frag = d.materials().values().stream()
                    .filter(m -> m.kind().equals("FRAGMENT") && Integer.valueOf(fragmentItem).equals(m.itemTemplateId()) && m.enabled() && m.buyCurrency().equals("GOLD")).findFirst().orElse(null);
            if (frag == null) {
                return fail(ActionError.DISABLED, "this piece is not sold");
            }
            long total = goldUnitPrice(d, frag, ch.level()) * t.piecesNeeded();
            if (total <= 0) {
                return fail(ActionError.DISABLED, "no price set");
            }
            if (ch.gold() < total) {
                return fail(ActionError.NOT_ENOUGH_GOLD, "not enough gold");
            }
            if (bagCount(c, ch.accountId()) >= bagLimit(d)) {
                return fail(ActionError.BAG_FULL, "bag is full");
            }
            spend(c, ch, "GOLD", total, "BUY_GEAR", "item:" + itemTemplateId);
            long itemId;
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_instance (account_id, template_id, slot) VALUES (?,?,?)", Statement.RETURN_GENERATED_KEYS)) {
                ps.setLong(1, ch.accountId());
                ps.setInt(2, itemTemplateId);
                ps.setString(3, t.slot());
                ps.executeUpdate();
                try (ResultSet rs = ps.getGeneratedKeys()) {
                    rs.next();
                    itemId = rs.getLong(1);
                }
            }
            itemLog(c, ch.id(), "BUY", "ITEM", itemTemplateId, itemId, 1, null);
            return ok(itemId, true);
        });
    }

    public ActionResult assembleItem(long accountId, long characterId, int itemTemplateId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.ItemTemplate t = d.items().get(itemTemplateId);
            if (t == null || !t.enabled()) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            if (ch.level() < t.minLevel()) {
                return fail(ActionError.LEVEL_TOO_LOW, "level too low");
            }
            int fragmentItem = d.fragmentRoot(t).id();
            StaticData.Material frag = d.materials().values().stream()
                    .filter(m -> m.kind().equals("FRAGMENT") && Integer.valueOf(fragmentItem).equals(m.itemTemplateId())).findFirst().orElse(null);
            if (frag == null) {
                return fail(ActionError.NOT_FOUND, "no fragments exist for this item");
            }
            if (materialQty(c, ch.accountId(), frag.id()) < t.piecesNeeded()) {
                return fail(ActionError.NOT_ENOUGH_MATERIAL, "not enough fragments");
            }
            if (bagCount(c, ch.accountId()) >= bagLimit(d)) {
                return fail(ActionError.BAG_FULL, "bag is full");
            }
            addMaterial(c, ch.accountId(), frag.id(), -t.piecesNeeded());
            long itemId;
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_instance (account_id, template_id, slot) VALUES (?,?,?)", Statement.RETURN_GENERATED_KEYS)) {
                ps.setLong(1, ch.accountId());
                ps.setInt(2, itemTemplateId);
                ps.setString(3, t.slot());
                ps.executeUpdate();
                try (ResultSet rs = ps.getGeneratedKeys()) {
                    rs.next();
                    itemId = rs.getLong(1);
                }
            }
            itemLog(c, ch.id(), "ASSEMBLE", "ITEM", itemTemplateId, itemId, 1, null);
            return ok(itemId, true);
        });
    }

    public ActionResult equipItem(long accountId, long characterId, long itemId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            ItemRowLite it = lockItem(c, ch.accountId(), itemId);
            if (it == null || !it.location.equals("BAG")) {
                return fail(ActionError.NOT_FOUND, "item not in the bag");
            }
            StaticData.ItemTemplate t = d.items().get(it.templateId);
            if (t == null || !GEAR_SLOTS.contains(it.slot)) {
                return fail(ActionError.WRONG_SLOT, "not a gear item");
            }
            // one bag holds every piece of the account, but a piece is worn by a character of its own planet (owner decision 2026-10-10, docs 15.18)
            if (!t.race().equals(ch.race())) {
                return fail(ActionError.RACE_LOCKED, "this gear is for another planet");
            }
            if (ch.level() < t.minLevel()) {
                return fail(ActionError.LEVEL_TOO_LOW, "level too low");
            }
            try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET location = 'BAG', version = version + 1 "
                    + "WHERE item_id = (SELECT item_id FROM character_equipment WHERE character_id = ? AND slot = ?)")) {
                ps.setLong(1, ch.id());
                ps.setString(2, it.slot);
                ps.executeUpdate();     // the piece that was worn goes back to the bag (the new piece leaves it, so the count does not grow)
            }
            try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET location = 'EQUIPPED', version = version + 1 WHERE item_id = ?")) {
                ps.setLong(1, itemId);
                ps.executeUpdate();
            }
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_equipment (character_id, slot, item_id) VALUES (?,?,?) ON DUPLICATE KEY UPDATE item_id = VALUES(item_id)")) {
                ps.setLong(1, ch.id());
                ps.setString(2, it.slot);
                ps.setLong(3, itemId);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    public ActionResult unequipItem(long accountId, long characterId, String slot) {
        return tx(accountId, characterId, (c, d, ch) -> {
            if (!GEAR_SLOTS.contains(slot)) {
                return fail(ActionError.WRONG_SLOT, "not a gear slot");
            }
            if (bagCount(c, ch.accountId()) >= bagLimit(d)) {
                return fail(ActionError.BAG_FULL, "bag is full");
            }
            Long worn = null;
            try (PreparedStatement ps = c.prepareStatement("SELECT item_id FROM character_equipment WHERE character_id = ? AND slot = ? FOR UPDATE")) {
                ps.setLong(1, ch.id());
                ps.setString(2, slot);
                try (ResultSet rs = ps.executeQuery()) {
                    if (rs.next() && rs.getObject(1) != null) {
                        worn = rs.getLong(1);
                    }
                }
            }
            if (worn == null) {
                return fail(ActionError.NOT_FOUND, "nothing worn in that slot");
            }
            try (PreparedStatement ps = c.prepareStatement("DELETE FROM character_equipment WHERE character_id = ? AND slot = ?")) {
                ps.setLong(1, ch.id());
                ps.setString(2, slot);
                ps.executeUpdate();
            }
            try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET location = 'BAG', version = version + 1 WHERE item_id = ?")) {
                ps.setLong(1, worn);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    public ActionResult sellItems(long accountId, long characterId, List<Long> itemIds, boolean destroy) {
        return tx(accountId, characterId, (c, d, ch) -> {
            Set<Long> unique = new HashSet<>(itemIds);
            if (unique.isEmpty() || unique.size() > 200) {
                return fail(ActionError.INVALID_ARGUMENT, "choose 1 to 200 items");
            }
            long perItem = 0;
            if (!destroy) {
                Map<String, Object> eco = d.balanceObj("item_economy_wins");
                perItem = Math.round(Json.num(eco, "sell_fraction", 0.05) * Json.num(eco, "item_buy", 6.0) * winGold(d, ch.level()));
            }
            long gold = 0;
            for (long id : unique) {
                ItemRowLite it = lockItem(c, ch.accountId(), id);
                if (it == null || !it.location.equals("BAG")) {
                    return fail(ActionError.NOT_FOUND, "item " + id + " is not in the bag");   // equipped items cannot be sold or destroyed (CONFIRMED)
                }
                try (PreparedStatement ps = c.prepareStatement("DELETE FROM item_instance WHERE item_id = ?")) {
                    ps.setLong(1, id);
                    ps.executeUpdate();
                }
                itemLog(c, ch.id(), destroy ? "DESTROY" : "SELL", "ITEM", it.templateId, id, -1, null);
                gold += perItem;
            }
            if (gold > 0) {
                earn(c, ch, "GOLD", gold, "SELL_ITEM", unique.size() + " item(s)");
            }
            return ok();
        });
    }

    public ActionResult upgradeItem(long accountId, long characterId, long itemId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            ItemRowLite it = lockItem(c, ch.accountId(), itemId);
            if (it == null) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            double max = d.balanceNum("upgrade_stone_max_pct", 75);
            double step = d.balanceNum("upgrade_stone_step_pct", 5);
            if (it.upgradePct + step > max + 1e-9) {
                return fail(ActionError.MAXED, "already at the maximum upgrade");
            }
            StaticData.Material stone = d.materials().values().stream().filter(m -> m.kind().equals("UPGRADE_STONE")).findFirst().orElse(null);
            if (stone == null || materialQty(c, ch.accountId(), stone.id()) < 1) {
                return fail(ActionError.NOT_ENOUGH_MATERIAL, "no upgrade stone");
            }
            addMaterial(c, ch.accountId(), stone.id(), -1);
            try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET upgrade_percent = upgrade_percent + ?, version = version + 1 WHERE item_id = ?")) {
                ps.setDouble(1, step);
                ps.setLong(2, itemId);
                ps.executeUpdate();
            }
            itemLog(c, ch.id(), "UPGRADE", "ITEM", it.templateId, itemId, 1, null);
            return ok(itemId, true);
        });
    }

    // ---- GEN (docs 15.21) ----------------------------------------------------------------------------------------------------

    /**
     * A GEN stone changes one line of a piece to another random set (owner decision 2026-10-10). The new set is among the sets of the planet of the piece, never one the piece
     * already carries on another line, and never the one the line has now; an empty line gets a set the first time.
     */
    public ActionResult rerollGen(long accountId, long characterId, long itemId, int lineNo) {
        return tx(accountId, characterId, (c, d, ch) -> {
            ItemRowLite it = lockItem(c, ch.accountId(), itemId);
            StaticData.ItemTemplate t = it == null ? null : d.items().get(it.templateId);
            if (t == null) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            GenSets.Rules rules = GenSets.rules(d);
            int slots = rules.initialSlots() + genExtraSlots(c, itemId);
            if (lineNo < 0 || lineNo >= slots) {
                return fail(ActionError.INVALID_ARGUMENT, "no such GEN line");
            }
            Map<Integer, String> lines = genLines(c, itemId);
            List<String> candidates = new ArrayList<>();
            for (GenSets.GenSet g : GenSets.forRace(d, t.race())) {
                boolean carried = false;
                for (Map.Entry<Integer, String> e : lines.entrySet()) {
                    carried |= e.getValue().equals(g.key());      // the same line (no change) or another line (no repeat on a piece)
                }
                if (!carried) {
                    candidates.add(g.key());
                }
            }
            if (candidates.isEmpty()) {
                return fail(ActionError.DISABLED, "no other set for this piece");
            }
            StaticData.Material stone = d.materials().values().stream().filter(m -> m.kind().equals("GEN_STONE")).findFirst().orElse(null);
            if (stone == null || materialQty(c, ch.accountId(), stone.id()) < rules.rerollStones()) {
                return fail(ActionError.NOT_ENOUGH_MATERIAL, "not enough GEN stones");
            }
            addMaterial(c, ch.accountId(), stone.id(), -rules.rerollStones());
            String picked = candidates.get(random.nextInt(candidates.size()));
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,?,?) ON DUPLICATE KEY UPDATE gen_key = VALUES(gen_key)")) {
                ps.setLong(1, itemId);
                ps.setInt(2, lineNo);
                ps.setString(3, picked);
                ps.executeUpdate();
            }
            touchItem(c, itemId);
            itemLog(c, ch.id(), "GEN_REROLL", "ITEM", it.templateId, itemId, 1, null);
            return ok(itemId, true);
        });
    }

    /** GEN slot stones open one more GEN line on a piece (up to the maximum of gen_rules); the cost grows with each line. */
    public ActionResult unlockGenSlot(long accountId, long characterId, long itemId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            ItemRowLite it = lockItem(c, ch.accountId(), itemId);
            if (it == null || d.items().get(it.templateId) == null) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            GenSets.Rules rules = GenSets.rules(d);
            int extra = genExtraSlots(c, itemId);
            int cost = rules.unlockCost(rules.initialSlots() + extra);
            if (cost <= 0) {
                return fail(ActionError.MAXED, "no more GEN lines can be opened");
            }
            StaticData.Material stone = d.materials().values().stream().filter(m -> m.kind().equals("GEN_SLOT_STONE")).findFirst().orElse(null);
            if (stone == null || materialQty(c, ch.accountId(), stone.id()) < cost) {
                return fail(ActionError.NOT_ENOUGH_MATERIAL, "not enough GEN slot stones");
            }
            addMaterial(c, ch.accountId(), stone.id(), -cost);
            try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET gen_extra_slots = gen_extra_slots + 1, version = version + 1 WHERE item_id = ?")) {
                ps.setLong(1, itemId);
                ps.executeUpdate();
            }
            itemLog(c, ch.id(), "GEN_SLOT", "ITEM", it.templateId, itemId, 1, null);
            return ok(itemId, true);
        });
    }

    private static int genExtraSlots(Connection c, long itemId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT gen_extra_slots FROM item_instance WHERE item_id = ?")) {
            ps.setLong(1, itemId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getInt(1) : 0;
            }
        }
    }

    private static Map<Integer, String> genLines(Connection c, long itemId) throws SQLException {
        Map<Integer, String> out = new java.util.TreeMap<>();
        try (PreparedStatement ps = c.prepareStatement("SELECT line_no, gen_key FROM item_gen_line WHERE item_id = ?")) {
            ps.setLong(1, itemId);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    out.put(rs.getInt(1), rs.getString(2));
                }
            }
        }
        return out;
    }

    private static void touchItem(Connection c, long itemId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("UPDATE item_instance SET version = version + 1 WHERE item_id = ?")) {
            ps.setLong(1, itemId);
            ps.executeUpdate();
        }
    }

    // ---- support items, crafting ---------------------------------------------------------------------------------------------

    public ActionResult buySupport(long accountId, long characterId, int supportId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.SupportItem s = d.supportItems().get(supportId);
            if (s == null) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            if (!s.enabled() || !s.obtain().equals("SHOP") || s.gemPrice() == null) {
                return fail(ActionError.DISABLED, "not for sale");
            }
            if (!raceFits(s.race(), ch.race())) {
                return fail(ActionError.RACE_LOCKED, "for another race");
            }
            if (exists(c, "SELECT 1 FROM character_support_item WHERE account_id = ? AND support_id = ?", ch.accountId(), supportId)) {
                return fail(ActionError.ALREADY_OWNED, "already owned");
            }
            if (ch.gems() < s.gemPrice()) {
                return fail(ActionError.NOT_ENOUGH_GEMS, "not enough gems");
            }
            spend(c, ch, "GEM", s.gemPrice(), "BUY_SUPPORT", "support:" + supportId);
            long id = insertSupport(c, ch.accountId(), supportId, true);
            itemLog(c, ch.id(), "BUY", "SUPPORT", supportId, null, 1, null);
            return ok(id, true);
        });
    }

    public ActionResult equipSupport(long accountId, long characterId, long instanceId) {
        return tx(accountId, characterId, (c, d, ch) -> {
            int supportId = supportOf(c, ch.accountId(), instanceId);
            StaticData.SupportItem s = supportId < 0 ? null : d.supportItems().get(supportId);
            if (s == null) {
                return fail(ActionError.NOT_FOUND, "item not found");
            }
            String slot = SUPPORT_SLOT.get(s.kind());
            if (slot == null || !s.enabled()) {
                return fail(ActionError.DISABLED, "cannot be worn");
            }
            if (!raceFits(s.race(), ch.race())) {
                return fail(ActionError.RACE_LOCKED, "for another race");
            }
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_equipment (character_id, slot, support_instance_id) VALUES (?,?,?) "
                    + "ON DUPLICATE KEY UPDATE support_instance_id = VALUES(support_instance_id)")) {
                ps.setLong(1, ch.id());
                ps.setString(2, slot);
                ps.setLong(3, instanceId);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    public ActionResult unequipSupport(long accountId, long characterId, String slot) {
        return tx(accountId, characterId, (c, d, ch) -> {
            if (!SUPPORT_SLOT.containsValue(slot)) {
                return fail(ActionError.WRONG_SLOT, "not a support slot");
            }
            try (PreparedStatement ps = c.prepareStatement("DELETE FROM character_equipment WHERE character_id = ? AND slot = ?")) {
                ps.setLong(1, ch.id());
                ps.setString(2, slot);
                return ps.executeUpdate() == 0 ? fail(ActionError.NOT_FOUND, "nothing worn in that slot") : ok();
            }
        });
    }

    /** Crafting a skill book (CRAFT) or turning book 1 into book 2 (UPGRADE): docs/database/BattleBall.md 10.25. */
    public ActionResult craft(long accountId, long characterId, int recipeId, long inputInstanceId, boolean useBoost) {
        return tx(accountId, characterId, (c, d, ch) -> {
            StaticData.Recipe r = d.recipes().stream().filter(x -> x.id() == recipeId).findFirst().orElse(null);
            if (r == null || !r.enabled()) {
                return fail(ActionError.NOT_FOUND, "recipe not found");
            }
            StaticData.SupportItem result = d.supportItems().get(r.resultSupportId());
            if (result == null || !raceFits(result.race(), ch.race())) {
                return fail(ActionError.RACE_LOCKED, "this recipe is for another race");
            }
            boolean upgrade = r.type().equals("UPGRADE");
            if (upgrade) {
                int have = supportOf(c, ch.accountId(), inputInstanceId);
                if (have < 0 || r.inputSupportId() == null || have != r.inputSupportId()) {
                    return fail(ActionError.NOT_OWNED, "the book to upgrade is missing");
                }
            }
            for (StaticData.RecipeInput in : r.inputs()) {
                if (materialQty(c, ch.accountId(), in.materialId()) < in.quantity()) {
                    return fail(ActionError.NOT_ENOUGH_MATERIAL, "not enough materials");
                }
            }
            boolean boost = useBoost && r.boostMaterialId() != null;
            if (boost && materialQty(c, ch.accountId(), r.boostMaterialId()) < 1) {
                return fail(ActionError.NOT_ENOUGH_MATERIAL, "no seal");
            }
            double chance = boost && r.boostSuccessPct() != null ? r.boostSuccessPct() : r.successPct();
            boolean success = random.nextDouble() * 100.0 < chance;
            for (StaticData.RecipeInput in : r.inputs()) {
                long lose = success ? in.quantity() : (long) Math.ceil(in.quantity() * in.loseOnFailPct() / 100.0);
                if (lose > 0) {
                    addMaterial(c, ch.accountId(), in.materialId(), -lose);
                }
            }
            if (boost) {
                addMaterial(c, ch.accountId(), r.boostMaterialId(), -1);
            }
            long newId = 0;
            if (success) {
                if (upgrade) {
                    try (PreparedStatement ps = c.prepareStatement("UPDATE character_support_item SET support_id = ? WHERE instance_id = ?")) {
                        ps.setInt(1, r.resultSupportId());
                        ps.setLong(2, inputInstanceId);
                        ps.executeUpdate();
                    }
                    newId = inputInstanceId;
                } else {
                    newId = insertSupport(c, ch.accountId(), r.resultSupportId(), true);
                    int lines = result.bookLines();
                    if (boost && random.nextDouble() * 100.0 < r.extraLineChancePct()) {
                        lines += r.extraLinesMin() + random.nextInt(Math.max(1, r.extraLinesMax() - r.extraLinesMin() + 1));
                    }
                    rollBookLines(c, d, newId, lines);
                }
            }
            itemLog(c, ch.id(), upgrade ? "UPGRADE" : "CRAFT", "SUPPORT", r.resultSupportId(), null, success ? 1 : 0, null);
            return ok(newId, success);
        });
    }

    private void rollBookLines(Connection c, StaticData d, long instanceId, int lines) throws SQLException {
        List<StaticData.BookPoolEntry> pool = new ArrayList<>(d.bookPool().stream().filter(e -> d.statEnabled(e.statKey())).toList());
        for (int line = 1; line <= lines && !pool.isEmpty(); line++) {
            int total = pool.stream().mapToInt(StaticData.BookPoolEntry::weight).sum();
            int pick = random.nextInt(Math.max(1, total));
            StaticData.BookPoolEntry chosen = pool.get(pool.size() - 1);
            for (StaticData.BookPoolEntry e : pool) {
                pick -= e.weight();
                if (pick < 0) {
                    chosen = e;
                    break;
                }
            }
            double value = Math.round((chosen.min() + random.nextDouble() * (chosen.max() - chosen.min())) * 10.0) / 10.0;
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_support_stat (instance_id, line_no, stat_key, `value`) VALUES (?,?,?,?)")) {
                ps.setLong(1, instanceId);
                ps.setInt(2, line);
                ps.setString(3, chosen.statKey());
                ps.setDouble(4, value);
                ps.executeUpdate();
            }
            pool.remove(chosen);    // one stat appears at most once on a book
        }
    }

    // ---- Potara ----------------------------------------------------------------------------------------------------------

    public ActionResult buyPotaraShards(long accountId, long characterId, int quantity) {
        return tx(accountId, characterId, (c, d, ch) -> {
            Map<String, Object> cfg = d.balanceObj("potara");
            int cap = (int) Json.num(cfg, "daily_cap", 20);
            int needed = (int) Json.num(cfg, "shards", 100);
            long price = (long) Json.num(cfg, "shard_price_gold", 15000);
            int owned;
            boolean has;
            try (PreparedStatement ps = c.prepareStatement("SELECT potara_owned, potara_fragments FROM account WHERE account_id = ? FOR UPDATE")) {
                ps.setLong(1, accountId);
                try (ResultSet rs = ps.executeQuery()) {
                    rs.next();
                    has = rs.getBoolean(1);
                    owned = rs.getInt(2);
                }
            }
            if (has) {
                return fail(ActionError.DISABLED, "the earring is already owned");   // no more shards once the earring exists (CONFIRMED)
            }
            if (quantity < 1 || owned + quantity > needed) {
                return fail(ActionError.INVALID_ARGUMENT, "quantity not valid");
            }
            int bought = 0;
            try (PreparedStatement ps = c.prepareStatement("SELECT cnt FROM daily_purchase_counter WHERE account_id = ? AND shop_item_id = ? AND day_key = ? FOR UPDATE")) {
                ps.setLong(1, accountId);
                ps.setInt(2, POTARA_SHARD_ID);
                ps.setObject(3, PlayerRepository.today());
                try (ResultSet rs = ps.executeQuery()) {
                    if (rs.next()) {
                        bought = rs.getInt(1);
                    }
                }
            }
            if (bought + quantity > cap) {
                return fail(ActionError.DAILY_LIMIT, "daily limit reached");
            }
            long total = price * quantity;
            if (ch.gold() < total) {
                return fail(ActionError.NOT_ENOUGH_GOLD, "not enough gold");
            }
            spend(c, ch, "GOLD", total, "POTARA_SHARD", "x" + quantity);
            try (PreparedStatement ps = c.prepareStatement("UPDATE account SET potara_fragments = potara_fragments + ? WHERE account_id = ?")) {
                ps.setInt(1, quantity);
                ps.setLong(2, accountId);
                ps.executeUpdate();
            }
            try (PreparedStatement ps = c.prepareStatement("INSERT INTO daily_purchase_counter (account_id, shop_item_id, day_key, cnt) VALUES (?,?,?,?) "
                    + "ON DUPLICATE KEY UPDATE cnt = cnt + VALUES(cnt)")) {
                ps.setLong(1, accountId);
                ps.setInt(2, POTARA_SHARD_ID);
                ps.setObject(3, PlayerRepository.today());
                ps.setInt(4, quantity);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    public ActionResult assemblePotara(long accountId, long anyCharacterId) {
        return tx(accountId, anyCharacterId, (c, d, ch) -> {
            int needed = (int) Json.num(d.balanceObj("potara"), "shards", 100);
            try (PreparedStatement ps = c.prepareStatement("SELECT potara_owned, potara_fragments FROM account WHERE account_id = ? FOR UPDATE")) {
                ps.setLong(1, accountId);
                try (ResultSet rs = ps.executeQuery()) {
                    rs.next();
                    if (rs.getBoolean(1)) {
                        return fail(ActionError.ALREADY_OWNED, "the earring is already owned");   // one earring per account (CONFIRMED)
                    }
                    if (rs.getInt(2) < needed) {
                        return fail(ActionError.NOT_ENOUGH_MATERIAL, "not enough shards");
                    }
                }
            }
            try (PreparedStatement ps = c.prepareStatement("UPDATE account SET potara_owned = 1, potara_fragments = potara_fragments - ? WHERE account_id = ?")) {
                ps.setInt(1, needed);
                ps.setLong(2, accountId);
                ps.executeUpdate();
            }
            return ok();
        });
    }

    // ---- inventory paging ---------------------------------------------------------------------------------------------------

    public InventoryPage inventory(long accountId, long characterId, long afterItemId, int pageSize) throws SQLException {
        StaticData d = data.get();
        int size = pageSize <= 0 ? (int) Json.num(d.balanceObj("inventory_rules"), "bag_page_size", 100) : Math.min(pageSize, 500);
        try (Connection c = db.connection()) {
            if (!exists(c, "SELECT 1 FROM game_character WHERE character_id = ? AND account_id = ?", characterId, accountId)) {
                return InventoryPage.newBuilder().setCharacterId(characterId).build();
            }
            Map<Long, Map<String, Double>> lines = new java.util.HashMap<>();
            try (PreparedStatement ps = c.prepareStatement("SELECT l.item_id, l.stat_key, l.`value` FROM item_stat_line l JOIN item_instance i USING (item_id) "
                    + "WHERE i.account_id = ? AND i.location = 'BAG' AND i.item_id > ?")) {
                ps.setLong(1, accountId);
                ps.setLong(2, afterItemId);
                try (ResultSet rs = ps.executeQuery()) {
                    while (rs.next()) {
                        lines.computeIfAbsent(rs.getLong(1), k -> new java.util.LinkedHashMap<>()).merge(rs.getString(2), rs.getDouble(3), Double::sum);
                    }
                }
            }
            Map<Long, Map<Integer, String>> gens = new java.util.HashMap<>();
            try (PreparedStatement ps = c.prepareStatement("SELECT g.item_id, g.line_no, g.gen_key FROM item_gen_line g JOIN item_instance i USING (item_id) "
                    + "WHERE i.account_id = ? AND i.location = 'BAG' AND i.item_id > ?")) {
                ps.setLong(1, accountId);
                ps.setLong(2, afterItemId);
                try (ResultSet rs = ps.executeQuery()) {
                    while (rs.next()) {
                        gens.computeIfAbsent(rs.getLong(1), k -> new java.util.TreeMap<>()).put(rs.getInt(2), rs.getString(3));
                    }
                }
            }
            InventoryPage.Builder b = InventoryPage.newBuilder().setCharacterId(characterId);
            try (PreparedStatement ps = c.prepareStatement("SELECT item_id, template_id, slot, upgrade_percent, gen_extra_slots FROM item_instance WHERE account_id = ? AND location = 'BAG' "
                    + "AND item_id > ? ORDER BY item_id LIMIT ?")) {
                ps.setLong(1, accountId);
                ps.setLong(2, afterItemId);
                ps.setInt(3, size + 1);
                try (ResultSet rs = ps.executeQuery()) {
                    int n = 0;
                    while (rs.next()) {
                        if (n++ == size) {
                            b.setHasMore(true);
                            break;
                        }
                        b.addItems(PlayerRepository.itemProto(d, new PlayerRepository.ItemRow(rs.getLong(1), rs.getInt(2), rs.getString(3), rs.getDouble(4), "BAG",
                                lines.getOrDefault(rs.getLong(1), Map.of()), PlayerRepository.gensList(gens.get(rs.getLong(1))), rs.getInt(5))));
                    }
                }
            }
            b.setTotal(bagCount(c, accountId));
            return b.build();
        }
    }

    // ---- small helpers -----------------------------------------------------------------------------------------------------

    private record ItemRowLite(int templateId, String slot, double upgradePct, String location) {
    }

    private static ItemRowLite lockItem(Connection c, long accountId, long itemId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT template_id, slot, upgrade_percent, location FROM item_instance WHERE item_id = ? AND account_id = ? FOR UPDATE")) {
            ps.setLong(1, itemId);
            ps.setLong(2, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? new ItemRowLite(rs.getInt(1), rs.getString(2), rs.getDouble(3), rs.getString(4)) : null;
            }
        }
    }

    private static int supportOf(Connection c, long accountId, long instanceId) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT support_id FROM character_support_item WHERE instance_id = ? AND account_id = ? FOR UPDATE")) {
            ps.setLong(1, instanceId);
            ps.setLong(2, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getInt(1) : -1;
            }
        }
    }

    private static long insertSupport(Connection c, long accountId, int supportId, boolean appraised) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO character_support_item (account_id, support_id, appraised) VALUES (?,?,?)", Statement.RETURN_GENERATED_KEYS)) {
            ps.setLong(1, accountId);
            ps.setInt(2, supportId);
            ps.setBoolean(3, appraised);
            ps.executeUpdate();
            try (ResultSet rs = ps.getGeneratedKeys()) {
                rs.next();
                return rs.getLong(1);
            }
        }
    }

    private static boolean exists(Connection c, String sql, Object... params) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next();
            }
        }
    }
}
