package com.battleball.server;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.EconomyService;
import com.battleball.server.data.StaticData;
import com.battleball.server.game.PlayerRepository;
import com.battleball.server.game.StatCalculator;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.List;
import java.util.UUID;
import java.util.random.RandomGenerator;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The shop, gear, crafting and Potara rules against a real database (the real migrations and seed data). */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class EconomyServiceTest {
    private static TestDb testDb;
    private static Database db;
    private static StaticDataStore data;
    private static AccountRepository accounts;
    private static final FixedRandom RANDOM = new FixedRandom();
    private static EconomyService eco;

    /** Random numbers we control: nextDouble() returns {@code value}; nextInt() returns 0. */
    static final class FixedRandom implements RandomGenerator {
        volatile double value = 0.0;

        @Override
        public long nextLong() {
            return 0;
        }

        @Override
        public double nextDouble() {
            return value;
        }

        @Override
        public int nextInt(int bound) {
            return 0;
        }
    }

    @BeforeAll
    static void start() throws Exception {
        testDb = TestDb.create();
        db = new Database(testDb.url, testDb.user, testDb.password, 4);
        data = new StaticDataStore(new StaticDataLoader(db));
        accounts = new AccountRepository(db);
        eco = new EconomyService(db, data, RANDOM);
    }

    @AfterAll
    static void stop() throws Exception {
        db.close();
        testDb.close();
    }

    /** A new account with its 3 characters; returns {accountId, earthId, namecId, xaydaId}. */
    private static long[] newAccount() throws Exception {
        String user = "e" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
        accounts.createAccount(user, "$2a$04$abcdefghijklmnopqrstuuabcdefghijklmnopqrstuvwxyz01234");
        long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
        long[] out = new long[4];
        out[0] = account;
        out[1] = scalar("SELECT character_id FROM game_character WHERE account_id = ? AND race = 'EARTH'", account);
        out[2] = scalar("SELECT character_id FROM game_character WHERE account_id = ? AND race = 'NAMEC'", account);
        out[3] = scalar("SELECT character_id FROM game_character WHERE account_id = ? AND race = 'XAYDA'", account);
        return out;
    }

    private static void give(long characterId, long gold, long gems) throws Exception {
        // the wallet is the account's (shared by the three characters, V026): the character id only names the account
        update("UPDATE account_wallet w JOIN game_character c ON c.account_id = w.account_id SET w.gold = w.gold + ?, w.gems = w.gems + ? WHERE c.character_id = ?", gold, gems, characterId);
    }

    private static void giveMaterial(long characterId, int template, long qty) throws Exception {
        update("INSERT INTO material_stack (account_id, template_id, quantity) SELECT account_id, ?, ? FROM game_character WHERE character_id = ? "
                + "ON DUPLICATE KEY UPDATE quantity = quantity + VALUES(quantity)", template, qty, characterId);
    }

    private static long scalar(String sql, Object... params) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : -1;
            }
        }
    }

    private static void update(String sql, Object... params) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            ps.executeUpdate();
        }
    }

    private static PlayerRepository.CharacterData character(long account, long id) throws Exception {
        try (Connection c = db.connection()) {
            return PlayerRepository.load(c, account).character(id);
        }
    }

    private static PlayerRepository.PlayerData load(long account) throws Exception {
        try (Connection c = db.connection()) {
            return PlayerRepository.load(c, account);
        }
    }

    private static void assertError(ActionError expected, ActionResult r) {
        assertFalse(r.getOk(), "should have been refused");
        assertEquals(expected, r.getError(), r.getMessage());
    }

    // ---- stats ----
    @Test
    void statsFollowTheDesignTable() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        for (int[] row : new int[][]{{1, 288_945, 13_065, 11_055}, {50, 430_528, 19_467, 16_472}, {100, 575_000, 26_000, 22_000}}) {
            update("UPDATE game_character SET level = ? WHERE character_id = ?", row[0], a[1]);
            var s = PlayerRepository.stats(d, character(a[0], a[1]), false);
            assertEquals(row[1], s.hpMax(), "HP at level " + row[0]);
            assertEquals(row[1], s.kiMax());
            assertEquals(row[2], s.atk(), "ATK at level " + row[0]);
            assertEquals(row[3], s.armor(), "armor at level " + row[0]);
            assertEquals(0.1 * row[0], s.critRatePct(), 1e-9);
            assertEquals(1.0 * row[0], s.critDmgPct(), 1e-9);
        }
        assertEquals(0.4, StatCalculator.armorReduction(d, 33_333), 0.001);   // A / (A + 50,000)
    }

    // ---- skins and skills ----
    @Test
    void buyingAndWearingSkins() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        int earthSkin = d.skins().values().stream().filter(s -> s.kindKey().equals("SHOP") && s.genderId() == 0 && s.enabled() && s.gemPrice() != null).findFirst().orElseThrow().id();
        int xaydaSkin = d.skins().values().stream().filter(s -> s.kindKey().equals("SHOP") && s.genderId() == 2 && s.enabled() && s.gemPrice() != null).findFirst().orElseThrow().id();
        int price = d.skins().get(earthSkin).gemPrice();
        assertError(ActionError.NOT_ENOUGH_GEMS, eco.buySkin(a[0], a[1], earthSkin));
        give(a[1], 0, price + 10);
        assertError(ActionError.RACE_LOCKED, eco.buySkin(a[0], a[2], earthSkin));            // a Namek cannot buy an Earth skin
        assertError(ActionError.NOT_FOUND, eco.buySkin(a[0] + 9999, a[1], earthSkin));       // someone else's character
        assertTrue(eco.buySkin(a[0], a[1], earthSkin).getOk());
        assertEquals(10, character(a[0], a[1]).gems());
        assertError(ActionError.ALREADY_OWNED, eco.buySkin(a[0], a[1], earthSkin));
        assertError(ActionError.NOT_OWNED, eco.equipSkin(a[0], a[1], xaydaSkin));
        assertTrue(eco.equipSkin(a[0], a[1], earthSkin).getOk());
        assertEquals(earthSkin, character(a[0], a[1]).equipment().get("SKIN").costumeId());
        assertEquals(1, scalar("SELECT COUNT(*) FROM currency_ledger WHERE character_id = ? AND reason = 'BUY_SKIN' AND delta = ?", a[1], -price));
        int component = d.skins().values().stream().filter(s -> s.kindKey().equals("COMPONENT")).findFirst().orElseThrow().id();
        assertError(ActionError.DISABLED, eco.buySkin(a[0], a[1], component));                // avatars are never sold
    }

    @Test
    void learningAndRaisingASkillCostsGold() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        long cost1 = d.skills().get(1).level(1).goldCost();
        long cost2 = d.skills().get(1).level(2).goldCost();
        assertError(ActionError.NOT_ENOUGH_GOLD, eco.upgradeSkill(a[0], a[1], 1));
        assertError(ActionError.NOT_FOUND, eco.upgradeSkill(a[0], a[1], 3));                  // Masenko is a Namek skill
        give(a[1], cost1 + cost2, 0);
        assertTrue(eco.upgradeSkill(a[0], a[1], 1).getOk());
        assertTrue(eco.upgradeSkill(a[0], a[1], 1).getOk());
        assertEquals(2, character(a[0], a[1]).skills().get(1));
        assertEquals(0, character(a[0], a[1]).gold());
    }

    @Test
    void theSnapshotCarriesTheSkillTableForTheSkillTab() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        var snap = PlayerRepository.toProto(d, load(a[0]), 10);
        assertEquals(d.skills().values().stream().filter(StaticData.Skill::enabled).count(), snap.getSkillCatalogCount(), "every enabled skill is listed");
        var kame = snap.getSkillCatalogList().stream().filter(e -> e.getSkillId() == 1).findFirst().orElseThrow();
        assertEquals("EARTH", kame.getRace());
        assertEquals(7, kame.getMaxLevel());
        assertEquals(7, kame.getLevelsCount());
        assertEquals(d.skills().get(1).level(1).goldCost(), kame.getLevels(0).getGoldCost());
        assertTrue(kame.getIconId() > 0 && !kame.getName().isEmpty() && !kame.getDescription().isEmpty());
        var ultimate = snap.getSkillCatalogList().stream().filter(e -> e.getSkillId() == 24).findFirst().orElseThrow();
        assertEquals(9, ultimate.getMaxLevel(), "the ultimates go to level 9 (GDD 4)");
    }

    // ---- gear ----
    @Test
    void fragmentsBecomeGearThatChangesStatsAndCanBeSold() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        giveMaterial(a[1], 103, 10);                                                          // 10 glove fragments
        long before = PlayerRepository.stats(d, character(a[0], a[1]), false).atk();
        ActionResult made = eco.assembleItem(a[0], a[1], 3);
        assertTrue(made.getOk());
        assertError(ActionError.NOT_ENOUGH_MATERIAL, eco.assembleItem(a[0], a[1], 3));
        long itemId = made.getNewId();
        assertTrue(eco.equipItem(a[0], a[1], itemId).getOk());
        long after = PlayerRepository.stats(d, character(a[0], a[1]), false).atk();
        assertEquals(before + 20_000, after, "gloves add 20,000 ATK");

        giveMaterial(a[1], 201, 2);
        assertTrue(eco.upgradeItem(a[0], a[1], itemId).getOk());                              // +5% of the base stat
        assertEquals(before + 21_000, PlayerRepository.stats(d, character(a[0], a[1]), false).atk());
        assertEquals(1, scalar("SELECT quantity FROM material_stack WHERE account_id = ? AND template_id = 201", a[0]));

        assertError(ActionError.NOT_FOUND, eco.sellItems(a[0], a[1], List.of(itemId), false));   // a worn item cannot be sold (CONFIRMED)
        assertTrue(eco.unequipItem(a[0], a[1], "GLOVES").getOk());
        long goldBefore = character(a[0], a[1]).gold();
        assertTrue(eco.sellItems(a[0], a[1], List.of(itemId), false).getOk());
        assertEquals(300, character(a[0], a[1]).gold() - goldBefore, "5% of 6 wins at level 1: 0.05 x 6 x 1000 = 300");
        assertEquals(0, scalar("SELECT COUNT(*) FROM item_instance WHERE item_id = ?", itemId));
    }

    @Test
    void equippingTwoPiecesOfOneSlotSwapsThemAndTheBagLimitHolds() throws Exception {
        long[] a = newAccount();
        giveMaterial(a[1], 101, 20);
        long first = eco.assembleItem(a[0], a[1], 1).getNewId();
        long second = eco.assembleItem(a[0], a[1], 1).getNewId();
        assertTrue(eco.equipItem(a[0], a[1], first).getOk());
        assertTrue(eco.equipItem(a[0], a[1], second).getOk());
        assertEquals("BAG", location(first));
        assertEquals("EQUIPPED", location(second));
        // fill the bag to the limit: assembling must stop, equipped pieces do not count
        update("UPDATE balance_config SET value_json = '{\"bag_limit\":3,\"bag_warn\":2,\"bag_page_size\":100,\"revives_per_match\":2}' WHERE config_key = 'inventory_rules'");
        data.refreshIfChanged();   // the version did not change, so force the swap by hand
        update("UPDATE static_data_version SET version = version + 1 WHERE domain = 'BALANCE'");
        assertTrue(data.refreshIfChanged());
        giveMaterial(a[1], 102, 30);
        assertTrue(eco.assembleItem(a[0], a[1], 2).getOk());    // bag: first + 1 = 2
        assertTrue(eco.assembleItem(a[0], a[1], 2).getOk());    // 3
        assertError(ActionError.BAG_FULL, eco.assembleItem(a[0], a[1], 2));
        update("UPDATE balance_config SET value_json = '{\"bag_limit\":2000,\"bag_warn\":1800,\"bag_page_size\":100,\"revives_per_match\":2}' WHERE config_key = 'inventory_rules'");
        update("UPDATE static_data_version SET version = version + 1 WHERE domain = 'BALANCE'");
        data.refreshIfChanged();
    }

    private static String location(long itemId) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement("SELECT location FROM item_instance WHERE item_id = ?")) {
            ps.setLong(1, itemId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getString(1);
            }
        }
    }

    // ---- materials ----
    @Test
    void buyingMaterialsPricesFollowTheLevel() throws Exception {
        long[] a = newAccount();
        update("UPDATE game_character SET level = 50 WHERE character_id = ?", a[1]);
        give(a[1], 1_000_000, 0);
        assertTrue(eco.buyMaterial(a[0], a[1], 101, 10).getOk());            // 0.6 win x 50,000 gold = 30,000 each
        assertEquals(700_000, character(a[0], a[1]).gold());
        assertEquals(10, character(a[0], a[1]).materials().get(101));
        assertError(ActionError.DISABLED, eco.buyMaterial(a[0], a[1], 301, 1));          // book materials are drops only
        assertError(ActionError.DISABLED, eco.buyMaterial(a[0], a[1], 1820, 1));          // Potara shards have their own request
        assertError(ActionError.INVALID_ARGUMENT, eco.buyMaterial(a[0], a[1], 101, 0));
        assertError(ActionError.NOT_ENOUGH_GOLD, eco.buyMaterial(a[0], a[1], 201, 1000));
    }

    // ---- support items and crafting ----
    @Test
    void supportItemsAreBoughtWithGemsAndWornInTheirSlot() throws Exception {
        long[] a = newAccount();
        give(a[1], 0, 1000);
        ActionResult pet = eco.buySupport(a[0], a[1], 1224);                                  // a pet, 150 gems
        assertTrue(pet.getOk());
        assertError(ActionError.ALREADY_OWNED, eco.buySupport(a[0], a[1], 1224));
        assertError(ActionError.DISABLED, eco.buySupport(a[0], a[1], 1044));                  // skill books are crafted, not sold
        assertTrue(eco.equipSupport(a[0], a[1], pet.getNewId()).getOk());
        assertEquals(pet.getNewId(), character(a[0], a[1]).equipment().get("SUPPORT_1").supportInstanceId());
        assertTrue(eco.unequipSupport(a[0], a[1], "SUPPORT_1").getOk());
        assertEquals(850, character(a[0], a[1]).gems());
    }

    @Test
    void craftingASkillBookWithAndWithoutTheSeal() throws Exception {
        long[] a = newAccount();
        giveMaterial(a[1], 301, 30);
        giveMaterial(a[1], 302, 10);
        giveMaterial(a[1], 303, 1);
        assertError(ActionError.RACE_LOCKED, eco.craft(a[0], a[2], 1, 0, false));             // recipe 1 is the Earth book
        RANDOM.value = 0.99;                                                                   // 99% roll: the 20% attempt fails
        ActionResult fail = eco.craft(a[0], a[1], 1, 0, false);
        assertTrue(fail.getOk());
        assertFalse(fail.getSuccess());
        var ch = character(a[0], a[1]);
        assertEquals(25, ch.materials().get(301), "a failed try loses half of the old books (5 of 10)");
        assertEquals(9, ch.materials().get(302), "and the paper punch");
        assertTrue(ch.supports().isEmpty());

        RANDOM.value = 0.0;                                                                    // 0% roll: success
        ActionResult win = eco.craft(a[0], a[1], 1, 0, false);
        assertTrue(win.getSuccess());
        ch = character(a[0], a[1]);
        assertEquals(15, ch.materials().get(301));
        assertEquals(8, ch.materials().get(302));
        assertEquals(1, ch.supports().size());
        assertEquals(1044, ch.supports().get(0).supportId());
        assertEquals(1, ch.supports().get(0).lines().size(), "a book has one stat line");

        RANDOM.value = 0.5;                                                                    // the seal makes it 100%, and 50% < 20% is false: no extra line
        ActionResult sealed = eco.craft(a[0], a[1], 1, 0, true);
        assertTrue(sealed.getSuccess());
        ch = character(a[0], a[1]);
        assertFalse(ch.materials().containsKey(303), "the seal is used up");
        assertEquals(2, ch.supports().size());
        assertError(ActionError.NOT_ENOUGH_MATERIAL, eco.craft(a[0], a[1], 1, 0, true));     // no seal left
    }

    @Test
    void upgradingBookOneToBookTwo() throws Exception {
        long[] a = newAccount();
        giveMaterial(a[1], 301, 10);
        giveMaterial(a[1], 302, 21);
        RANDOM.value = 0.0;
        long book = eco.craft(a[0], a[1], 1, 0, false).getNewId();
        assertError(ActionError.NOT_OWNED, eco.craft(a[0], a[1], 4, 0, false));               // the book to upgrade is not named
        RANDOM.value = 0.99;
        assertFalse(eco.craft(a[0], a[1], 4, book, false).getSuccess());
        assertEquals(10, character(a[0], a[1]).materials().get(302), "the punches are lost on failure, 10 of 20");
        assertEquals(1044, character(a[0], a[1]).supports().get(0).supportId(), "the book is kept");
        RANDOM.value = 0.0;
        assertTrue(eco.craft(a[0], a[1], 4, book, false).getSuccess());
        assertEquals(1278, character(a[0], a[1]).supports().get(0).supportId());
        assertEquals(1, character(a[0], a[1]).supports().get(0).lines().size(), "the stat line stays");
    }

    // ---- Potara ----
    @Test
    void potaraShardsDailyLimitAndTheEarring() throws Exception {
        long[] a = newAccount();
        give(a[1], 10_000_000, 0);
        assertError(ActionError.INVALID_ARGUMENT, eco.buyPotaraShards(a[0], a[1], 0));
        assertTrue(eco.buyPotaraShards(a[0], a[1], 20).getOk());                               // 20 x 15,000 gold, the daily cap
        assertEquals(10_000_000 - 300_000, character(a[0], a[1]).gold());
        assertError(ActionError.DAILY_LIMIT, eco.buyPotaraShards(a[0], a[1], 1));
        assertError(ActionError.NOT_ENOUGH_MATERIAL, eco.assemblePotara(a[0], a[1]));
        update("UPDATE account SET potara_fragments = 100 WHERE account_id = ?", a[0]);
        assertTrue(eco.assemblePotara(a[0], a[1]).getOk());
        assertTrue(scalar("SELECT potara_owned FROM account WHERE account_id = ?", a[0]) == 1);
        assertEquals(0, scalar("SELECT potara_fragments FROM account WHERE account_id = ?", a[0]));
        assertError(ActionError.ALREADY_OWNED, eco.assemblePotara(a[0], a[1]));               // one earring per account
        update("DELETE FROM daily_purchase_counter WHERE account_id = ?", a[0]);
        assertError(ActionError.DISABLED, eco.buyPotaraShards(a[0], a[1], 1));                // no more shards once the earring exists
    }

    @Test
    void inventoryPaging() throws Exception {
        long[] a = newAccount();
        giveMaterial(a[1], 102, 70);
        for (int i = 0; i < 7; i++) {
            assertTrue(eco.assembleItem(a[0], a[1], 2).getOk());
        }
        var p1 = eco.inventory(a[0], a[1], 0, 3);
        assertEquals(3, p1.getItemsCount());
        assertTrue(p1.getHasMore());
        assertEquals(7, p1.getTotal());
        var p2 = eco.inventory(a[0], a[1], p1.getItems(2).getItemId(), 3);
        assertEquals(3, p2.getItemsCount());
        var p3 = eco.inventory(a[0], a[1], p2.getItems(2).getItemId(), 3);
        assertEquals(1, p3.getItemsCount());
        assertFalse(p3.getHasMore());
        assertEquals(0, eco.inventory(a[0] + 777, a[1], 0, 3).getItemsCount(), "another account sees nothing");
    }

    // ---- one inventory for the three characters (V026) ----
    @Test
    void theThreeCharactersShareOneInventory() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        // wallet and materials belong to the account: what the Earth character gets, the Xayda character sees
        give(a[1], 100_000, 7);
        giveMaterial(a[1], 101, 20);
        assertEquals(100_000, character(a[0], a[3]).gold());
        assertEquals(7, character(a[0], a[3]).gems());
        assertEquals(20, character(a[0], a[3]).materials().get(101));
        // gold spent by one character is gone for the others
        update("UPDATE game_character SET level = 30 WHERE character_id = ?", a[2]);
        assertTrue(eco.buyMaterial(a[0], a[2], 101, 1).getOk());
        assertTrue(character(a[0], a[1]).gold() < 100_000, "the Namek's purchase was paid from the shared wallet");

        // a piece of gear for Xayda (template 31, made from the fragments of the armor) assembled by the Earth character sits in the shared bag and is worn by the Xayda character:
        // a piece is worn by a character of its own planet (owner decision 2026-10-10); only one wearer at a time
        long item = eco.assembleItem(a[0], a[1], 31).getNewId();
        assertEquals(1, character(a[0], a[3]).bagCount(), "the bag is shared");
        assertError(ActionError.RACE_LOCKED, eco.equipItem(a[0], a[1], item));            // the Earth character cannot wear Xayda gear
        assertTrue(eco.equipItem(a[0], a[3], item).getOk());
        assertEquals(item, character(a[0], a[3]).equipment().get("ARMOR").itemId().longValue());
        assertError(ActionError.NOT_FOUND, eco.equipItem(a[0], a[2], item));              // worn by Xayda: not in the bag
        assertTrue(eco.unequipItem(a[0], a[3], "ARMOR").getOk());
        assertTrue(eco.equipItem(a[0], a[3], item).getOk());
        assertEquals(0, character(a[0], a[3]).bagCount());

        // skins are owned by the account and worn by the race they fit
        int earthSkin = d.skins().values().stream().filter(s -> s.kindKey().equals("SHOP") && s.genderId() == 0 && s.enabled() && s.gemPrice() != null).findFirst().orElseThrow().id();
        give(a[1], 0, d.skins().get(earthSkin).gemPrice());
        assertTrue(eco.buySkin(a[0], a[1], earthSkin).getOk());
        assertTrue(character(a[0], a[3]).ownedSkins().contains(earthSkin), "the Xayda character sees the Earth skin in the shared bag");
        assertError(ActionError.RACE_LOCKED, eco.equipSkin(a[0], a[3], earthSkin));
        assertTrue(eco.equipSkin(a[0], a[1], earthSkin).getOk());
    }

    // ---- the fusion skin belongs to the fusion form (V028) ----
    @Test
    void aFusionSkinIsWornByTheFusionFormAndCountsOnlyThere() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        var fusionSkin = d.skins().values().stream().filter(s -> s.kindKey().equals("FUSION_FORM") && "td".equals(s.fusionRace()) && s.enabled()).findFirst().orElseThrow();
        int fusion = fusionSkin.id();
        update("INSERT INTO owned_skin (account_id, costume_id, source) VALUES (?,?,'ADMIN')", a[0], fusion);
        assertError(ActionError.DISABLED, eco.equipSkin(a[0], a[1], fusion, false));          // not in the character's own slot
        assertError(ActionError.RACE_LOCKED, eco.equipSkin(a[0], a[2], fusion, true));        // a Namek cannot hold the Earth fusion skin
        int plain = d.defaultSkinsByRace().get("EARTH").get(0);
        assertError(ActionError.DISABLED, eco.equipSkin(a[0], a[1], plain, true));            // an ordinary skin is not a fusion skin
        assertTrue(eco.equipSkin(a[0], a[1], fusion, true).getOk());
        var ch = character(a[0], a[1]);
        assertEquals(fusion, ch.equipment().get("FUSION_SKIN").costumeId());
        assertEquals(d.equippedDefaultSkinByRace().get("EARTH"), ch.equipment().get("SKIN").costumeId(), "the look of the character itself did not change");
        var alone = PlayerRepository.stats(d, ch, false);
        var fused = PlayerRepository.stats(d, ch, true);
        if (!fusionSkin.stats().isEmpty()) {
            assertTrue(fused.atk() >= alone.atk() && fused.hpMax() >= alone.hpMax());
            assertTrue(fused.atk() > alone.atk() || fused.hpMax() > alone.hpMax() || !fused.other().equals(alone.other()), "the fusion skin adds its stats in a fusion");
        }
        assertEquals(alone.atk(), PlayerRepository.stats(d, ch, false).atk(), "and nothing outside a fusion");

        // "Hỗ trợ 5": costume_id 0 takes the skin of a slot off
        assertTrue(eco.equipSkin(a[0], a[1], 0, true).getOk());
        assertTrue(character(a[0], a[1]).equipment().get("FUSION_SKIN") == null, "the fusion skin is off");
        assertTrue(eco.equipSkin(a[0], a[1], 0, false).getOk());
        assertTrue(character(a[0], a[1]).equipment().get("SKIN") == null, "the character's own skin is off");
        assertTrue(eco.equipSkin(a[0], a[1], 0, false).getOk(), "taking off twice is harmless");
        assertTrue(eco.equipSkin(a[0], a[1], plain, false).getOk(), "and it can be worn again");
    }

    // ---- GEN (docs 15.21) ----
    private static long gearPiece(long account, String race, int tier, String slot) throws Exception {
        int template = (int) scalar("SELECT template_id FROM item_template WHERE race = ? AND tier = ? AND slot = ?", race, tier, slot);
        update("INSERT INTO item_instance (account_id, template_id, slot) VALUES (?,?,?)", account, template, slot);
        return scalar("SELECT MAX(item_id) FROM item_instance WHERE account_id = ?", account);
    }

    private static String genOf(long account, long character, long itemId, int line) throws Exception {
        var page = eco.inventory(account, character, 0, 0);
        for (var it : page.getItemsList()) {
            if (it.getItemId() == itemId) {
                return it.getGens(line).getGenKey();
            }
        }
        throw new AssertionError("item not in the bag");
    }

    @Test
    void aGenStoneChangesAChosenLineToAnotherSetOfThePlanetAndAnotherStoneOpensALine() throws Exception {
        long[] a = newAccount();
        long item = gearPiece(a[0], "EARTH", 1, "ARMOR");
        var first = eco.inventory(a[0], a[1], 0, 0).getItems(0);
        assertEquals(1, first.getGenSlots(), "one empty line to begin with");
        assertEquals("", first.getGens(0).getGenKey());
        assertEquals(1, first.getGenUnlockCost(), "the second line costs one slot stone");
        assertError(ActionError.NOT_ENOUGH_MATERIAL, eco.rerollGen(a[0], a[1], item, 0));
        giveMaterial(a[1], 204, 5);
        giveMaterial(a[1], 205, 3);
        assertError(ActionError.INVALID_ARGUMENT, eco.rerollGen(a[0], a[1], item, 1));          // only one line yet
        assertTrue(eco.rerollGen(a[0], a[1], item, 0).getOk());
        String one = genOf(a[0], a[1], item, 0);
        assertFalse(one.isEmpty());
        var set = com.battleball.server.game.GenSets.find(data.get(), one);
        assertTrue(set.race().equals("EARTH") || set.race().equals("ALL"), "only a set of the planet of the piece");
        assertEquals(4, character(a[0], a[1]).materials().get(204).intValue(), "one GEN stone spent");
        assertTrue(eco.unlockGenSlot(a[0], a[1], item).getOk());
        assertEquals(2, eco.inventory(a[0], a[1], 0, 0).getItems(0).getGenSlots());
        assertEquals(2, eco.inventory(a[0], a[1], 0, 0).getItems(0).getGenUnlockCost(), "the third line costs two");
        assertTrue(eco.rerollGen(a[0], a[1], item, 1).getOk());
        String two = genOf(a[0], a[1], item, 1);
        assertFalse(two.isEmpty() || two.equals(one), "two lines of a piece never carry the same set");
        for (int i = 0; i < 6; i++) {                                                        // whatever the draw, the line changes and never copies the other line
            String before = genOf(a[0], a[1], item, 0);
            if (character(a[0], a[1]).materials().getOrDefault(204, 0L) < 1) {
                giveMaterial(a[1], 204, 1);
            }
            assertTrue(eco.rerollGen(a[0], a[1], item, 0).getOk());
            String after = genOf(a[0], a[1], item, 0);
            assertFalse(after.equals(before) || after.equals(genOf(a[0], a[1], item, 1)));
        }
        assertTrue(eco.unlockGenSlot(a[0], a[1], item).getOk());
        assertEquals(0, eco.inventory(a[0], a[1], 0, 0).getItems(0).getGenUnlockCost(), "three lines is the most");
        assertError(ActionError.MAXED, eco.unlockGenSlot(a[0], a[1], item));
        assertEquals(0, character(a[0], a[1]).materials().getOrDefault(205, 0L).intValue(), "1 + 2 slot stones spent");
    }

    @Test
    void fivePiecesOfASetSwitchItOnAndTheStepsFollowTheNumberWorn() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        String[] slots = {"ARMOR", "PANTS", "GLOVES", "BOOTS", "RING"};
        long[] items = new long[5];
        for (int i = 0; i < 5; i++) {
            items[i] = gearPiece(a[0], "XAYDA", 1, slots[i]);
            assertTrue(eco.equipItem(a[0], a[3], items[i]).getOk());
        }
        long hpPlain = PlayerRepository.stats(d, character(a[0], a[3]), false).hpMax();
        for (int i = 0; i < 4; i++) {
            update("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,0,'NAPPA')", items[i]);
        }
        assertEquals(hpPlain, PlayerRepository.stats(d, character(a[0], a[3]), false).hpMax(), "four pieces of Nappa do nothing");
        update("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,0,'NAPPA')", items[4]);
        var five = PlayerRepository.stats(d, character(a[0], a[3]), false);
        assertEquals(hpPlain * 1.8, five.hpMax(), hpPlain * 0.001, "five pieces of Nappa: +80% HP");
        var snapshotChar = PlayerRepository.toProto(d, load(a[0]), 20).getCharactersList().stream().filter(c -> c.getRace().equals("XAYDA")).findFirst().orElseThrow();
        assertEquals("NAPPA", snapshotChar.getActiveGens(0).getGenKey());
        assertEquals(5, snapshotChar.getActiveGens(0).getPieces());
        assertTrue(snapshotChar.getBonusesList().stream().noneMatch(l -> l.getStatKey().contains(":")), "the numbers of one skill stay on the server");

        // Ca Đíc M has steps: 2 pieces, 4 pieces and 5 pieces; a step replaces the same kind of effect of the lower one
        for (int i = 0; i < 5; i++) {
            update("DELETE FROM item_gen_line WHERE item_id = ?", items[i]);
        }
        for (int worn = 1; worn <= 5; worn++) {
            update("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,0,'CADIC_M')", items[worn - 1]);
            var s = PlayerRepository.stats(d, character(a[0], a[3]), false);
            assertEquals(worn >= 2 ? 200.0 : 0.0, s.other("SKILL_RADIUS_PX:14"), "wider blast from 2 pieces");
            assertEquals(worn == 5 ? 50.0 : worn == 4 ? 20.0 : 0.0, s.other("SKILL_DMG_HPMAX_PCT:14"), "the blast adds 20% from 4 pieces and 50% (not 70%) from 5");
        }
        // a piece with two lines counts for each of its sets, once
        update("INSERT INTO item_gen_line (item_id, line_no, gen_key) VALUES (?,1,'GOHAN')", items[0]);
        assertEquals(1, com.battleball.server.game.GenSets.counts(character(a[0], a[3]).equippedItems().stream().map(PlayerRepository.ItemRow::gens).toList()).get("GOHAN"));
    }

    // ---- throwing a skin away ----
    @Test
    void aSkinCanBeThrownAwayOnlyWhenNoCharacterWearsIt() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        int fusion = d.skins().values().stream().filter(s -> s.kindKey().equals("FUSION_FORM") && "td".equals(s.fusionRace()) && s.enabled()).findFirst().orElseThrow().id();
        update("INSERT INTO owned_skin (account_id, costume_id, source) VALUES (?,?,'ADMIN')", a[0], fusion);
        assertError(ActionError.NOT_OWNED, eco.discardSkin(a[0], a[1], 999999));
        assertTrue(eco.equipSkin(a[0], a[1], fusion, true).getOk());
        assertError(ActionError.DISABLED, eco.discardSkin(a[0], a[2], fusion));                 // worn by the Earth character, whichever character asks
        assertTrue(eco.equipSkin(a[0], a[1], 0, true).getOk());
        assertTrue(eco.discardSkin(a[0], a[2], fusion).getOk());
        assertTrue(!character(a[0], a[1]).ownedSkins().contains(fusion), "the skin is gone from the shared bag");
        assertError(ActionError.NOT_OWNED, eco.discardSkin(a[0], a[1], fusion));
    }

    // ---- auras (V029): unlocked by the highest character, one at a time, shared by the account ----
    @Test
    void anAuraIsWornWhenTheHighestCharacterReachedItsLevel() throws Exception {
        long[] a = newAccount();
        var d = data.get();
        var auras = com.battleball.server.game.Auras.all(d);
        assertTrue(auras.size() >= 60, "the auras of the owner's table are in the data");
        var low = auras.stream().filter(x -> x.enabled() && x.level() == 1).findFirst().orElseThrow();
        var high = auras.stream().filter(x -> x.enabled() && x.level() >= 90).findFirst().orElseThrow();
        assertError(ActionError.NOT_FOUND, eco.equipAura(a[0], a[1], 999999));
        assertTrue(eco.equipAura(a[0], a[1], low.id()).getOk(), "a level 1 aura is there from the start");
        assertEquals(low.id(), scalar("SELECT equipped_aura_id FROM account WHERE account_id = ?", a[0]));
        assertError(ActionError.LEVEL_TOO_LOW, eco.equipAura(a[0], a[2], high.id()));
        assertEquals(low.id(), scalar("SELECT equipped_aura_id FROM account WHERE account_id = ?", a[0]), "a refused aura changes nothing");
        update("UPDATE game_character SET level = ? WHERE character_id = ?", high.level(), a[3]);        // only one of the three characters has the level
        assertTrue(eco.equipAura(a[0], a[1], high.id()).getOk(), "the highest character of the account counts, whichever character asks");
        var snapshot = PlayerRepository.toProto(data.get(), load(a[0]), 5);
        assertEquals(high.id(), snapshot.getEquippedAuraId());
        assertEquals(auras.stream().filter(x -> x.enabled()).count(), snapshot.getAurasCount());
        assertTrue(eco.equipAura(a[0], a[1], 0).getOk(), "0 takes it off");
        assertEquals(0, PlayerRepository.toProto(data.get(), load(a[0]), 5).getEquippedAuraId());
    }
}
