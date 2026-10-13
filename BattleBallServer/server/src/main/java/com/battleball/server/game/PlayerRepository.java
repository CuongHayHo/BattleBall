package com.battleball.server.game;

import com.battleball.proto.AuraInfo;
import com.battleball.proto.AuraLayer;
import com.battleball.proto.CharacterInfo;
import com.battleball.proto.CharacterStats;
import com.battleball.proto.EquipSlotInfo;
import com.battleball.proto.ItemInfo;
import com.battleball.proto.MaterialAmount;
import com.battleball.proto.PlayerDataSnapshot;
import com.battleball.proto.PotaraInfo;
import com.battleball.proto.SkillState;
import com.battleball.proto.SkinInfo;
import com.battleball.proto.StatLine;
import com.battleball.proto.SupportInstance;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.LocalDate;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;

/** Reads a player's data from MariaDB and turns it into stats and into the PlayerDataSnapshot message. */
public final class PlayerRepository {
    private PlayerRepository() {
    }

    /** One extra line of a piece: 1 to 3 the stat lines of the stat stone, 4 the crystal (red) line (docs 15.27). */
    public record LineRow(int lineNo, String key, double value, String source) {
    }

    /** {@code lines} is the sum by stat (what the stats of the character add up); {@code lineRows} are the lines themselves (a stat may appear on two of them). */
    public record ItemRow(long id, int templateId, String slot, double upgradePct, String location, Map<String, Double> lines, List<String> gens, int extraSlots,
                          List<LineRow> lineRows) {
        public ItemRow(long id, int templateId, String slot, double upgradePct, String location, Map<String, Double> lines, List<String> gens, int extraSlots) {
            this(id, templateId, slot, upgradePct, location, lines, gens, extraSlots, List.of());
        }

        public ItemRow(long id, int templateId, String slot, double upgradePct, String location, Map<String, Double> lines) {
            this(id, templateId, slot, upgradePct, location, lines, List.of(), 0, List.of());
        }
    }

    /** Folds the rows of item_stat_line (item_id, line_no, stat_key, value, source) into the sum by stat and the list of lines of each piece. */
    public static void addLineRow(Map<Long, Map<String, Double>> sums, Map<Long, List<LineRow>> rows, ResultSet rs) throws SQLException {
        long item = rs.getLong(1);
        sums.computeIfAbsent(item, k -> new LinkedHashMap<>()).merge(rs.getString(3), rs.getDouble(4), Double::sum);
        rows.computeIfAbsent(item, k -> new ArrayList<>()).add(new LineRow(rs.getInt(2), rs.getString(3), rs.getDouble(4), rs.getString(5)));
    }

    public record SupportRow(long instanceId, int supportId, int chargePoints, boolean appraised, Map<String, Double> lines) {
    }

    public record EquipRef(Long itemId, Integer costumeId, Long supportInstanceId) {
    }

    /**
     * What the account's three characters share (V026): wallet, material stacks, owned skins, support items, gear in the bag.
     * Every CharacterData carries the same values, so the code that works on one character can read them without a second lookup.
     */
    public record Shared(long gold, long gems, Map<Integer, Long> materials, Set<Integer> ownedSkins, List<SupportRow> supports, int bagCount, int equippedAura) {
    }

    public record CharacterData(long id, long accountId, String race, int level, long exp, long gold, long gems, Map<Integer, Integer> skills,
                                Map<String, EquipRef> equipment, List<ItemRow> equippedItems, Map<Integer, Long> materials, Set<Integer> ownedSkins,
                                List<SupportRow> supports, int bagCount, Set<Integer> firstClears, Map<Integer, Long> cooldownReadyAt) {
        public SupportRow support(long instanceId) {
            return supports.stream().filter(s -> s.instanceId() == instanceId).findFirst().orElse(null);
        }
    }

    public record Potara(boolean owned, int fragments, int boughtToday) {
    }

    public record PlayerData(long accountId, String username, List<CharacterData> characters, Potara potara, Shared shared) {
        public CharacterData character(long id) {
            return characters.stream().filter(c -> c.id() == id).findFirst().orElse(null);
        }
    }

    /** The shop date changes at 00:00 in Vietnam (UTC+7), CONFIRMED. */
    public static LocalDate today() {
        return LocalDate.now(ZoneOffset.ofHours(7));
    }

    public static PlayerData load(Connection c, long accountId) throws SQLException {
        String username;
        boolean owned;
        int fragments;
        int aura;
        try (PreparedStatement ps = c.prepareStatement("SELECT username, potara_owned, potara_fragments, equipped_aura_id FROM account WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    throw new SQLException("account not found: " + accountId);
                }
                username = rs.getString(1);
                owned = rs.getBoolean(2);
                fragments = rs.getInt(3);
                aura = rs.getInt(4);                                 // 0 when NULL: no aura worn
            }
        }
        int boughtToday = 0;
        try (PreparedStatement ps = c.prepareStatement("SELECT cnt FROM daily_purchase_counter WHERE account_id = ? AND shop_item_id = 1820 AND day_key = ?")) {
            ps.setLong(1, accountId);
            ps.setObject(2, today());
            try (ResultSet rs = ps.executeQuery()) {
                if (rs.next()) {
                    boughtToday = rs.getInt(1);
                }
            }
        }
        List<Long> ids = new ArrayList<>();
        Map<Long, String[]> head = new LinkedHashMap<>();
        Map<Long, long[]> nums = new HashMap<>();   // level, exp
        try (PreparedStatement ps = c.prepareStatement("SELECT character_id, race, level, exp FROM game_character WHERE account_id = ? ORDER BY character_id")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    long id = rs.getLong(1);
                    ids.add(id);
                    head.put(id, new String[]{rs.getString(2)});
                    nums.put(id, new long[]{rs.getInt(3), rs.getLong(4)});
                }
            }
        }
        Map<Long, Map<Integer, Integer>> skills = new HashMap<>();
        Map<Long, Map<String, EquipRef>> equipment = new HashMap<>();
        Map<Long, List<ItemRow>> equippedItems = new HashMap<>();
        Map<Long, Set<Integer>> firstClears = new HashMap<>();
        Map<Long, Map<Integer, Long>> cooldowns = new HashMap<>();
        // ---- shared by the account (V026)
        long gold = 0, gems = 0;
        try (PreparedStatement ps = c.prepareStatement("SELECT gold, gems FROM account_wallet WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                if (rs.next()) {
                    gold = rs.getLong(1);
                    gems = rs.getLong(2);
                }
            }
        }
        List<Long> owner = List.of(accountId);
        Map<Integer, Long> materials = new TreeMap<>();
        query(c, "SELECT template_id, quantity FROM material_stack WHERE quantity > 0 AND account_id = ?", owner, rs -> materials.put(rs.getInt(1), rs.getLong(2)));
        Set<Integer> skins = new HashSet<>();
        query(c, "SELECT costume_id FROM owned_skin WHERE account_id = ?", owner, rs -> skins.add(rs.getInt(1)));
        Map<Long, Map<String, Double>> bookLines = new HashMap<>();
        query(c, "SELECT s.instance_id, s.stat_key, s.`value` FROM character_support_stat s JOIN character_support_item i USING (instance_id) WHERE i.account_id = ?", owner,
                rs -> bookLines.computeIfAbsent(rs.getLong(1), k -> new LinkedHashMap<>()).merge(rs.getString(2), rs.getDouble(3), Double::sum));
        List<SupportRow> supports = new ArrayList<>();
        query(c, "SELECT instance_id, support_id, charge_points, appraised FROM character_support_item WHERE account_id = ? ORDER BY instance_id", owner,
                rs -> supports.add(new SupportRow(rs.getLong(1), rs.getInt(2), rs.getInt(3), rs.getBoolean(4), bookLines.getOrDefault(rs.getLong(1), Map.of()))));
        int[] bag = new int[1];
        query(c, "SELECT COUNT(*) FROM item_instance WHERE location = 'BAG' AND account_id = ?", owner, rs -> bag[0] = rs.getInt(1));
        Shared shared = new Shared(gold, gems, materials, skins, supports, bag[0], aura);
        if (!ids.isEmpty()) {
            String in = placeholders(ids.size());
            query(c, "SELECT character_id, skill_id, `level` FROM character_skill WHERE character_id IN (" + in + ")", ids,
                    rs -> skills.computeIfAbsent(rs.getLong(1), k -> new TreeMap<>()).put(rs.getInt(2), rs.getInt(3)));
            query(c, "SELECT character_id, slot, item_id, costume_id, support_instance_id FROM character_equipment WHERE character_id IN (" + in + ")", ids,
                    rs -> equipment.computeIfAbsent(rs.getLong(1), k -> new LinkedHashMap<>()).put(rs.getString(2),
                            new EquipRef(nullableLong(rs, 3), (Integer) nullableInt(rs, 4), nullableLong(rs, 5))));
            Map<Long, Map<String, Double>> itemLines = new HashMap<>();
            Map<Long, List<LineRow>> itemLineRows = new HashMap<>();
            query(c, "SELECT l.item_id, l.line_no, l.stat_key, l.`value`, l.source FROM item_stat_line l JOIN item_instance i USING (item_id) WHERE i.location = 'EQUIPPED' AND i.account_id = ? "
                    + "ORDER BY l.item_id, l.line_no", owner, rs -> addLineRow(itemLines, itemLineRows, rs));
            Map<Long, Map<Integer, String>> itemGens = new HashMap<>();
            query(c, "SELECT g.item_id, g.line_no, g.gen_key FROM item_gen_line g JOIN item_instance i USING (item_id) WHERE i.location = 'EQUIPPED' AND i.account_id = ?", owner,
                    rs -> itemGens.computeIfAbsent(rs.getLong(1), k -> new TreeMap<>()).put(rs.getInt(2), rs.getString(3)));
            query(c, "SELECT i.item_id, e.character_id, i.template_id, i.slot, i.upgrade_percent, i.location, i.gen_extra_slots FROM character_equipment e JOIN item_instance i ON i.item_id = e.item_id "
                    + "WHERE e.character_id IN (" + in + ")", ids,
                    rs -> equippedItems.computeIfAbsent(rs.getLong(2), k -> new ArrayList<>()).add(new ItemRow(rs.getLong(1), rs.getInt(3), rs.getString(4),
                            rs.getDouble(5), rs.getString(6), itemLines.getOrDefault(rs.getLong(1), Map.of()), gensList(itemGens.get(rs.getLong(1))), rs.getInt(7),
                            itemLineRows.getOrDefault(rs.getLong(1), List.of()))));
            query(c, "SELECT character_id, skill_id, ready_at_ms FROM character_skill_cooldown WHERE ready_at_ms > " + System.currentTimeMillis()
                    + " AND character_id IN (" + in + ")", ids,
                    rs -> cooldowns.computeIfAbsent(rs.getLong(1), k -> new TreeMap<>()).put(rs.getInt(2), rs.getLong(3)));
            query(c, "SELECT character_id, dungeon_id FROM character_first_clear WHERE character_id IN (" + in + ")", ids,
                    rs -> firstClears.computeIfAbsent(rs.getLong(1), k -> new HashSet<>()).add(rs.getInt(2)));
        }
        List<CharacterData> chars = new ArrayList<>();
        for (long id : ids) {
            long[] n = nums.get(id);
            chars.add(new CharacterData(id, accountId, head.get(id)[0], (int) n[0], n[1], shared.gold(), shared.gems(), skills.getOrDefault(id, Map.of()),
                    equipment.getOrDefault(id, Map.of()), equippedItems.getOrDefault(id, List.of()), shared.materials(),
                    shared.ownedSkins(), shared.supports(), shared.bagCount(), firstClears.getOrDefault(id, Set.of()), cooldowns.getOrDefault(id, Map.of())));
        }
        return new PlayerData(accountId, username, chars, new Potara(owned, fragments, boughtToday), shared);
    }

    // ---- stats -------------------------------------------------------------------------------------------------------

    private static void addSkinBonus(StaticData d, EquipRef ref, boolean fusionSlot, List<Map<String, Double>> out) {
        if (ref == null || ref.costumeId() == null) {
            return;
        }
        StaticData.Skin skin = d.skins().get(ref.costumeId());
        StaticData.SkinKind kind = skin == null ? null : d.skinKinds().get(skin.kindKey());
        if (skin != null && skin.enabled() && kind != null && kind.requiresFusionMain() == fusionSlot) {
            out.add(skin.stats());
        }
    }

    /**
     * Final stats of a character. {@code fusedMain} is true only when the character is the main one of a fusion in this match
     * (the fusion skin only counts then). Pet effects are temporary during a match and are not included here.
     */
    public static StatCalculator.CharacterStats stats(StaticData d, CharacterData ch, boolean fusedMain) {
        List<StatCalculator.GearPiece> gear = new ArrayList<>();
        for (ItemRow it : ch.equippedItems()) {
            StaticData.ItemTemplate t = d.items().get(it.templateId());
            if (t != null) {
                gear.add(new StatCalculator.GearPiece(t.baseStatKey(), t.baseValue() * (1 + it.upgradePct() / 100.0), it.lines(), it.gens()));
            }
        }
        List<Map<String, Double>> bonuses = new ArrayList<>();
        addSkinBonus(d, ch.equipment().get("SKIN"), false, bonuses);            // the skin worn on the character always counts
        if (fusedMain) {
            addSkinBonus(d, ch.equipment().get("FUSION_SKIN"), true, bonuses);  // the fusion skin only for the main character of a fusion
        }
        double atkMult = 1.0;
        for (int i = 1; i <= 4; i++) {
            EquipRef ref = ch.equipment().get("SUPPORT_" + i);
            if (ref == null || ref.supportInstanceId() == null) {
                continue;
            }
            SupportRow row = ch.support(ref.supportInstanceId());
            StaticData.SupportItem def = row == null ? null : d.supportItems().get(row.supportId());
            if (def == null || !def.enabled()) {
                continue;
            }
            bonuses.add(def.stats());
            bonuses.add(row.lines());
            if (def.train() != null) {
                // worn training armor weakens the wearer, as in NRO; the stored points pay out when it is taken off (see trainBonus)
                atkMult *= 1 - def.train().atkPenaltyPct() / 100.0;
            }
        }
        StatCalculator.CharacterStats base = StatCalculator.compute(d, ch.level(), gear, bonuses);
        // training armor that is NOT worn but still has points gives +ATK for that many matches
        double bonus = trainBonusPct(d, ch);
        double mult = atkMult * (1 + bonus / 100.0);
        return mult == 1.0 ? base : new StatCalculator.CharacterStats(base.hpMax(), base.kiMax(), Math.round(base.atk() * mult), base.armor(),
                base.critRatePct(), base.critDmgPct(), base.dodgePct(), base.hpRegenPctPerSec(), base.kiRegenPctPerSec(), base.other());
    }

    /** +ATK % from stored training-armor points while no training armor is worn (the best owned armor with points counts). */
    public static double trainBonusPct(StaticData d, CharacterData ch) {
        EquipRef worn = ch.equipment().get("SUPPORT_2");
        if (worn != null && worn.supportInstanceId() != null) {
            return 0;
        }
        double best = 0;
        for (SupportRow s : ch.supports()) {
            StaticData.SupportItem def = d.supportItems().get(s.supportId());
            if (def != null && def.train() != null && s.chargePoints() > 0) {
                best = Math.max(best, def.train().atkBonusPct());
            }
        }
        return best;
    }

    // ---- protocol message --------------------------------------------------------------------------------------------

    /** Exp needed to go from {@code level} to the next one (same rule as MatchSettlement: coef x level^power), 0 at the maximum level. */
    static long expToNext(StaticData d, int level) {
        int max = (int) Json.num(d.balanceObj("char_stats_lv100"), "max_level", 100);
        if (level >= max) {
            return 0;
        }
        Map<String, Object> rules = d.balanceObj("exp_rules");
        return Math.round(Json.num(rules, "coef", 100) * Math.pow(level, Json.num(rules, "power", 2)));
    }

    /**
     * How close to its target a skill is worth using, in Nro px between the centres (the dummy's radius is counted in): the same figures the AI allies use to decide
     * (MatchEngine.aiWants). 0 for the skills that need no target (shield, healing, buffs). The client walks the controlled character this close before it attacks.
     */
    public static double reachPx(StaticData.Skill sk, StaticData.SkillLevel L) {
        double reach = switch (sk.id()) {
            case 0, 2, 4, 9, 17 -> (L.lengthPx() == null ? 36 : L.lengthPx()) - 2 + 14;     // 14 = the hit radius of a training dummy
            case 1, 3, 5 -> (L.lengthPx() == null ? 300 : L.lengthPx()) + 14 - 20;
            case 24, 25, 11 -> (L.lengthPx() == null ? 200 : Math.min(L.lengthPx(), 600)) * 0.8;
            case 26 -> (L.radiusPx() == null ? 500 : L.radiusPx()) * 0.8;
            case 10 -> (L.radiusPx() == null ? 380 : L.radiusPx()) * 0.8;
            case 12 -> 600;
            case 18, 22, 23 -> L.radiusPx() == null ? 300 : L.radiusPx();
            default -> 0;
        };
        return Math.max(0, reach);
    }

    /** Writes the cooldowns of a session that ended (characterId to skill id to Unix ms); passed rows are removed. Blocking. */
    public static void saveCooldowns(Connection c, Map<Long, Map<Integer, Long>> readyAt) throws SQLException {
        long now = System.currentTimeMillis();
        try (PreparedStatement up = c.prepareStatement("INSERT INTO character_skill_cooldown (character_id, skill_id, ready_at_ms) VALUES (?,?,?) "
                + "ON DUPLICATE KEY UPDATE ready_at_ms = VALUES(ready_at_ms)");
             PreparedStatement del = c.prepareStatement("DELETE FROM character_skill_cooldown WHERE character_id = ? AND skill_id = ?")) {
            for (Map.Entry<Long, Map<Integer, Long>> ch : readyAt.entrySet()) {
                for (Map.Entry<Integer, Long> e : ch.getValue().entrySet()) {
                    PreparedStatement ps = e.getValue() > now ? up : del;
                    ps.setLong(1, ch.getKey());
                    ps.setInt(2, e.getKey());
                    if (ps == up) {
                        ps.setLong(3, e.getValue());
                    }
                    ps.executeUpdate();
                }
            }
        }
    }

    public static PlayerDataSnapshot toProto(StaticData d, PlayerData p, int potaraDailyCap) {
        PlayerDataSnapshot.Builder b = PlayerDataSnapshot.newBuilder().setAccountId(p.accountId()).setUsername(p.username());
        for (CharacterData ch : p.characters()) {
            StatCalculator.CharacterStats s = stats(d, ch, false);
            StatCalculator.CharacterStats fs = stats(d, ch, true);
            CharacterInfo.Builder cb = CharacterInfo.newBuilder().setCharacterId(ch.id()).setRace(ch.race()).setLevel(ch.level()).setExp(ch.exp())
                    .setExpToNext(expToNext(d, ch.level())).setDefaultCostumeId(d.equippedDefaultSkinByRace().getOrDefault(ch.race(), 0))
                    .setStats(CharacterStats.newBuilder().setHpMax(s.hpMax()).setKiMax(s.kiMax()).setAtk(s.atk()).setArmor(s.armor())
                            .setCritRatePct(s.critRatePct()).setCritDmgPct(s.critDmgPct()).setDodgePct(s.dodgePct())
                            .setHpRegenPctPerSec(s.hpRegenPctPerSec()).setKiRegenPctPerSec(s.kiRegenPctPerSec()));
            new TreeMap<>(s.other()).forEach((key, value) -> {
                if (key.indexOf(':') < 0) {
                    cb.addBonuses(StatLine.newBuilder().setStatKey(key).setValue(value).setSource("TOTAL"));
                }
            });
            for (Map.Entry<String, Integer> e : new TreeMap<>(GenSets.counts(ch.equippedItems().stream().map(ItemRow::gens).toList())).entrySet()) {
                if (!e.getKey().isEmpty()) {
                    cb.addActiveGens(com.battleball.proto.GenActive.newBuilder().setGenKey(e.getKey()).setPieces(e.getValue()));
                }
            }
            cb.setFusionStats(CharacterStats.newBuilder().setHpMax(fs.hpMax()).setKiMax(fs.kiMax()).setAtk(fs.atk()).setArmor(fs.armor())
                    .setCritRatePct(fs.critRatePct()).setCritDmgPct(fs.critDmgPct()).setDodgePct(fs.dodgePct())
                    .setHpRegenPctPerSec(fs.hpRegenPctPerSec()).setKiRegenPctPerSec(fs.kiRegenPctPerSec()));
            new TreeMap<>(fs.other()).forEach((key, value) -> {
                if (key.indexOf(':') < 0) {
                    cb.addFusionBonuses(StatLine.newBuilder().setStatKey(key).setValue(value).setSource("TOTAL"));
                }
            });
            ch.skills().forEach((id, lv) -> cb.addSkills(SkillState.newBuilder().setSkillId(id).setLevel(lv)));
            long nowMs = System.currentTimeMillis();
            ch.cooldownReadyAt().forEach((id, readyAt) -> {
                if (readyAt > nowMs) {
                    cb.addCooldowns(com.battleball.proto.SkillCooldown.newBuilder().setSkillId(id).setLeftMs((int) Math.min(Integer.MAX_VALUE, readyAt - nowMs)));
                }
            });
            ch.equipment().forEach((slot, ref) -> cb.addEquipment(EquipSlotInfo.newBuilder().setSlot(slot)
                    .setItemId(ref.itemId() == null ? 0 : ref.itemId()).setCostumeId(ref.costumeId() == null ? 0 : ref.costumeId())
                    .setSupportInstanceId(ref.supportInstanceId() == null ? 0 : ref.supportInstanceId())));
            for (ItemRow it : ch.equippedItems()) {
                cb.addEquippedItems(itemProto(d, it));
            }
            ch.firstClears().stream().sorted().forEach(cb::addFirstClears);
            b.addCharacters(cb);
        }
        for (GenSets.GenSet g : GenSets.all(d)) {
            com.battleball.proto.GenSetInfo.Builder gb = com.battleball.proto.GenSetInfo.newBuilder().setGenKey(g.key()).setName(g.name()).setRace(g.race());
            g.tiers().forEach(t -> gb.addTiers(com.battleball.proto.GenTierInfo.newBuilder().setPieces(t.pieces()).setText(t.text())));
            b.addGenSets(gb);
        }
        GenSets.Rules genRules = GenSets.rules(d);
        com.battleball.proto.GenRulesInfo.Builder rb = com.battleball.proto.GenRulesInfo.newBuilder().setRerollStones(genRules.rerollStones()).setMaxSlots(genRules.maxSlots());
        for (StaticData.Material m : d.materials().values()) {
            if (m.kind().equals("GEN_STONE")) {
                rb.setStoneTemplateId(m.id());
            } else if (m.kind().equals("GEN_SLOT_STONE")) {
                rb.setSlotStoneTemplateId(m.id());
            }
        }
        b.setGenRules(rb);
        StatStones.Rules ss = StatStones.rules(d);
        com.battleball.proto.StatStoneRulesInfo.Builder ssb = com.battleball.proto.StatStoneRulesInfo.newBuilder().setAddStones(ss.addStones()).setRaiseStones(ss.raiseStones())
                .setRetypeStones(ss.retypeStones()).setCrystalStones(ss.crystalStones()).setCrystalStart(ss.crystalStart());
        for (StaticData.Material m : d.materials().values()) {
            if (m.kind().equals("STAT_STONE")) {
                ssb.setStatStoneTemplateId(m.id());
            } else if (m.kind().equals("CRYSTAL_STONE")) {
                ssb.setCrystalStoneTemplateId(m.id());
            }
        }
        b.setStatStoneRules(ssb);
        Shared sh = p.shared();
        b.setGold(sh.gold()).setGems(sh.gems()).setBagCount(sh.bagCount());
        for (Auras.Aura a : Auras.all(d)) {
            if (!a.enabled()) {
                continue;
            }
            AuraInfo.Builder ab = AuraInfo.newBuilder().setAuraId(a.id()).setName(a.name()).setUnlockLevel(a.level());
            a.layers().forEach(l -> ab.addLayers(AuraLayer.newBuilder().setLayer(l.layer()).setFrames(l.frames())));
            b.addAuras(ab);
        }
        b.setEquippedAuraId(sh.equippedAura());
        // the skill table of the Kỹ năng tab: every enabled skill with the gold, damage, cooldown and Ki of each level
        d.skills().values().stream().filter(StaticData.Skill::enabled)
                .sorted(java.util.Comparator.comparing(StaticData.Skill::race).thenComparingInt(StaticData.Skill::slot)).forEach(sk -> {
                    com.battleball.proto.SkillCatalogEntry.Builder e = com.battleball.proto.SkillCatalogEntry.newBuilder().setSkillId(sk.id()).setRace(sk.race())
                            .setName(sk.name()).setIconId(Math.max(0, sk.iconId())).setSlot(sk.slot()).setMaxLevel(sk.maxLevel()).setDescription(sk.damInfo());
                    new TreeMap<>(sk.levels()).forEach((lv, L) -> e.addLevels(com.battleball.proto.SkillLevelInfo.newBuilder().setLevel(lv).setGoldCost(L.goldCost())
                            .setDamagePctAtk(L.damagePctAtk() == null ? 0 : L.damagePctAtk()).setCooldownMs(L.cooldownMs())
                            .setKiCostPct(L.kiCostPct() == null ? 0 : L.kiCostPct()).setRangePx((int) Math.round(reachPx(sk, L))).setCardCost(L.cardCost())));
                    b.addSkillCatalog(e);
                });
        // the dungeons of the list of the NPC Cui: enabled ones that have at least one real boss (minions do not count)
        Map<String, Object> tierLevels = Json.obj(d.balanceObj("match_rules"), "tier_level");
        d.dungeons().values().stream().filter(StaticData.Dungeon::enabled).sorted(java.util.Comparator.comparingInt(StaticData.Dungeon::id)).forEach(dg -> {
            long real = dg.bosses().stream().filter(x -> !x.role().equals("MINION")).filter(x -> {
                StaticData.Boss bs = d.bosses().get(x.bossId());
                return bs != null && bs.enabled();
            }).count();
            if (real == 0) {
                return;
            }
            DungeonSummary.Info what = DungeonSummary.of(d, dg);
            b.addDungeons(com.battleball.proto.DungeonInfo.newBuilder().setDungeonId(dg.id()).setName(dg.name()).setTier(dg.tier())
                    .setRecommendedLevel((int) Json.num(tierLevels, dg.tier(), 30)).setBossCount((int) Math.min(Math.max(1, dg.bossesPerMatch()), real))
                    .setTimeLimitS(dg.timeLimitS() == null ? 0 : dg.timeLimitS()).addAllBossNames(what.bosses()).setFinalBoss(what.finalBoss())
                    .addAllMinionNames(what.minions()).addAllDrops(what.drops()));
        });
        for (com.battleball.server.game.match.Difficulty df : com.battleball.server.game.match.Difficulty.all(d)) {                    // the difficulties of a dungeon (docs 15.33)
            b.addDifficulties(com.battleball.proto.DifficultyInfo.newBuilder().setKey(df.key()).setName(df.name()).setHpMult(df.hpMult()).setAtkMult(df.atkMult())
                    .setRewardMult(df.rewardMult()).setDropMult(df.dropMult()).setNote(df.note()));
        }
        sh.materials().forEach((id, q) -> {
            MaterialAmount.Builder mb = MaterialAmount.newBuilder().setTemplateId(id).setQuantity(q);
            StaticData.Material m = d.materials().get(id);
            if (m != null) {
                mb.setName(m.name()).setIconId(m.iconId()).setKind(m.kind()).setNote(m.note() == null ? "" : m.note());
            }
            b.addMaterials(mb);
        });
        sh.ownedSkins().stream().sorted().forEach(id -> b.addSkins(skinProto(d, id)));
        for (SupportRow sr : sh.supports()) {
            SupportInstance.Builder sb = SupportInstance.newBuilder().setInstanceId(sr.instanceId()).setSupportId(sr.supportId()).setChargePoints(sr.chargePoints());
            sr.lines().forEach((k, v) -> sb.addLines(StatLine.newBuilder().setStatKey(k).setValue(v).setSource("BOOK")));
            b.addSupports(sb);
        }
        b.setPotara(PotaraInfo.newBuilder().setOwned(p.potara().owned()).setFragments(p.potara().fragments()).setBoughtToday(p.potara().boughtToday())
                .setDailyCap(potaraDailyCap)
                .setItemId((int) Json.num(d.balanceObj("potara"), "item_id", 1819)).setIconId((int) Json.num(d.balanceObj("potara"), "icon_id", 15614))
                .setShardItemId((int) Json.num(d.balanceObj("potara"), "shard_item_id", 1820)).setShardIconId((int) Json.num(d.balanceObj("potara"), "shard_icon_id", 15613))
                .setShardsNeeded((int) Json.num(d.balanceObj("potara"), "shards", 100)).setMinFusionLevel((int) Json.num(d.balanceObj("potara"), "min_level", 50)));
        d.versions().forEach(b::putStaticVersions);
        return b.build();
    }

    /** An owned skin with its name, who may wear it and its stat bonuses (the popup of the bag shows them). */
    static SkinInfo skinProto(StaticData d, int costumeId) {
        StaticData.Skin skin = d.skins().get(costumeId);
        SkinInfo.Builder b = SkinInfo.newBuilder().setCostumeId(costumeId);
        if (skin == null) {
            return b.build();
        }
        StaticData.SkinKind kind = d.skinKinds().get(skin.kindKey());
        b.setName(skin.name()).setGenderId(skin.genderId()).setKind(skin.kindKey()).setFusionMainOnly(kind != null && kind.requiresFusionMain());
        if (skin.fusionRace() != null && !skin.fusionRace().equals("ANY")) {
            b.setFusionRace(skin.fusionRace());
        }
        new TreeMap<>(skin.stats()).forEach((k, v) -> b.addStats(StatLine.newBuilder().setStatKey(k).setValue(v).setSource("SKIN").setText(d.statText(k, v))));
        return b.build();
    }

    /** The GEN lines of a piece as a list by line number, "" for a line with nothing yet (the lines come from item_gen_line, which has no row for an empty one). */
    public static List<String> gensList(Map<Integer, String> byLine) {
        if (byLine == null || byLine.isEmpty()) {
            return List.of();
        }
        int last = java.util.Collections.max(byLine.keySet());
        List<String> out = new ArrayList<>();
        for (int i = 0; i <= last; i++) {
            out.add(byLine.getOrDefault(i, ""));
        }
        return out;
    }

    /** What a set does at each step, in one line ("2 món: ...; 5 món: ..."). */
    public static String genText(GenSets.GenSet g) {
        StringBuilder sb = new StringBuilder();
        for (GenSets.Tier t : g.tiers()) {
            if (sb.length() > 0) {
                sb.append("; ");
            }
            sb.append(t.pieces()).append(" món: ").append(t.text());
        }
        return sb.toString();
    }

    public static ItemInfo itemProto(StaticData d, ItemRow it) {
        ItemInfo.Builder b = ItemInfo.newBuilder().setItemId(it.id()).setTemplateId(it.templateId()).setSlot(it.slot()).setUpgradePercent(it.upgradePct())
                .setEquipped("EQUIPPED".equals(it.location()));
        StaticData.ItemTemplate t = d.items().get(it.templateId());
        if (t != null) {
            double main = t.baseValue() * (1 + it.upgradePct() / 100.0);
            b.setName(t.name()).setIconId(t.iconId()).setRace(t.race()).setTier(t.tier()).setMinLevel(t.minLevel())
                    .setMainStat(StatLine.newBuilder().setStatKey(t.baseStatKey()).setValue(main).setSource("BASE").setText(d.statText(t.baseStatKey(), main)));
        }
        StatStones.Rules stat = StatStones.rules(d);
        for (LineRow l : it.lineRows()) {
            double cap = StatStones.cap(stat, l);
            boolean red = StatStones.CRYSTAL_SOURCE.equals(l.source());
            StatStones.Type type = stat.type(l.key());
            b.addLines(StatLine.newBuilder().setStatKey(l.key()).setValue(l.value()).setSource(l.source()).setText(d.statText(l.key(), l.value())).setLineNo(l.lineNo())
                    .setCap(cap).setSuccessPct(stat.successPct(l.value(), cap))
                    .setStepMin(red ? stat.crystalStepMin() : type == null ? 0 : type.stepMin()).setStepMax(red ? stat.crystalStepMax() : type == null ? 0 : type.stepMax()));
        }
        b.setStatLinesMax(stat.maxLines());
        GenSets.Rules rules = GenSets.rules(d);
        int slots = rules.initialSlots() + it.extraSlots();
        for (int i = 0; i < slots; i++) {
            String key = i < it.gens().size() ? it.gens().get(i) : "";
            GenSets.GenSet g = key.isEmpty() ? null : GenSets.find(d, key);
            b.addGens(com.battleball.proto.GenLine.newBuilder().setLineNo(i).setGenKey(g == null ? "" : g.key()).setName(g == null ? "" : g.name()).setText(g == null ? "" : genText(g)));
        }
        b.setGenSlots(slots).setGenUnlockCost(rules.unlockCost(slots));
        return b.build();
    }

    // ---- small JDBC helpers ------------------------------------------------------------------------------------------

    @FunctionalInterface
    private interface RowHandler {
        void handle(ResultSet rs) throws SQLException;
    }

    private static void query(Connection c, String sql, List<Long> ids, RowHandler h) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < ids.size(); i++) {
                ps.setLong(i + 1, ids.get(i));
            }
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    h.handle(rs);
                }
            }
        }
    }

    private static String placeholders(int n) {
        return String.join(",", java.util.Collections.nCopies(n, "?"));
    }

    private static Long nullableLong(ResultSet rs, int col) throws SQLException {
        long v = rs.getLong(col);
        return rs.wasNull() ? null : v;
    }

    private static Object nullableInt(ResultSet rs, int col) throws SQLException {
        int v = rs.getInt(col);
        return rs.wasNull() ? null : v;
    }
}
