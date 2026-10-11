package com.battleball.server;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.QuestEntry;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.QuestService;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.time.LocalDate;
import java.util.List;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The quests of Ông Gôhan (docs 15.32) against a real database: the chain, the daily quests, the counters, the rewards. */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class QuestServiceTest {
    private static TestDb testDb;
    private static Database db;
    private static AccountRepository accounts;
    private static QuestService quests;
    private static final LocalDate[] TODAY = {LocalDate.of(2026, 10, 10)};

    @BeforeAll
    static void start() throws Exception {
        testDb = TestDb.create();
        db = new Database(testDb.url, testDb.user, testDb.password, 4);
        accounts = new AccountRepository(db);
        quests = new QuestService(db, new StaticDataStore(new StaticDataLoader(db)), () -> TODAY[0]);
    }

    @AfterAll
    static void stop() throws Exception {
        db.close();
        testDb.close();
    }

    private static long newAccount() throws Exception {
        String user = "q" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
        accounts.createAccount(user, "$2a$04$abcdefghijklmnopqrstuuabcdefghijklmnopqrstuvwxyz01234");
        return scalar("SELECT account_id FROM account WHERE username = ?", user);
    }

    private static long scalar(String sql, Object... params) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
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

    private static long cards(long account) throws Exception {
        return scalar("SELECT COALESCE(SUM(quantity), 0) FROM material_stack WHERE account_id = ? AND template_id = 206", account);
    }

    private static long gold(long account) throws Exception {
        return scalar("SELECT gold FROM account_wallet WHERE account_id = ?", account);
    }

    private static long gems(long account) throws Exception {
        return scalar("SELECT gems FROM account_wallet WHERE account_id = ?", account);
    }

    private static List<QuestEntry> entries(long account) throws Exception {
        try (Connection c = db.connection()) {
            return quests.entries(c, account);
        }
    }

    private static QuestEntry find(List<QuestEntry> list, String id) {
        return list.stream().filter(e -> e.getId().equals(id)).findFirst().orElse(null);
    }

    @Test
    void anAccountSeesTheFirstQuestOfTheChainAndTheDailyQuests() throws Exception {
        long a = newAccount();
        List<QuestEntry> list = entries(a);
        assertEquals(1, list.stream().filter(e -> e.getKind().equals("TUTORIAL")).count(), "only the quest being done of the chain");
        assertEquals(6, list.stream().filter(e -> e.getKind().equals("DAILY")).count());
        QuestEntry first = list.get(0);
        assertEquals("t01", first.getId());
        assertEquals("elder", first.getNpcId(), "the arrow of the village points to the elder");
        assertEquals(0, first.getProgress());
        assertEquals(1, first.getTarget());
        assertEquals(1000, first.getGold(), "one win of level 1");
        assertEquals(5, first.getGems());
        assertEquals(206, first.getItems(0).getTemplateId());
        assertEquals(3, first.getItems(0).getQuantity());
    }

    @Test
    void anEventCountsAndTheRewardIsPaidOnce() throws Exception {
        long a = newAccount();
        long gold0 = gold(a), gems0 = gems(a);
        assertEquals(ActionError.INVALID_ARGUMENT, quests.claim(a, "t01").getError(), "not done yet");
        assertTrue(quests.record(a, "ATTENDANCE", 1));
        assertEquals(1, find(entries(a), "t01").getProgress());
        assertEquals(1, find(entries(a), "d01").getProgress(), "the daily quest of the same event counts too");

        ActionResult r = quests.claim(a, "t01");
        assertTrue(r.getOk());
        assertEquals(gold0 + 1000, gold(a));
        assertEquals(gems0 + 5, gems(a));
        assertEquals(3, cards(a));
        assertEquals("t02", entries(a).get(0).getId(), "the chain moves on");
        assertFalse(quests.claim(a, "t01").getOk(), "paid once");
        assertEquals(3, cards(a));
        assertEquals(ActionError.INVALID_ARGUMENT, quests.claim(a, "t02").getError(), "the next quest is not done");
        assertEquals(ActionError.NOT_FOUND, quests.claim(a, "nothing").getError());
    }

    @Test
    void theWholeChainIsPaidAtOnceWhenEverythingWasDone() throws Exception {
        long a = newAccount();
        for (String type : new String[]{"ATTENDANCE", "UPGRADE_SKILL", "GET_GEAR", "EQUIP_ITEM", "UPGRADE_ITEM", "GEN_REROLL", "TRAINING", "DUNGEON_WIN", "BUY_SHARDS"}) {
            quests.record(a, type, 1);                               // the events count even before their quest shows (a player who did it already)
        }
        update("UPDATE game_character SET level = 50 WHERE account_id = ?", a);       // LEVEL quests are read from the account
        update("UPDATE account SET potara_owned = 1 WHERE account_id = ?", a);        // so is the earring
        assertTrue(quests.claim(a, "").getOk());
        assertEquals(92 + 12, cards(a), "the 92 cards of the 13 quests of the chain and the 12 of the five daily quests that were done (2 + 4 + 1 + 2 + 3)");
        assertEquals(0, entries(a).stream().filter(e -> e.getKind().equals("TUTORIAL")).count(), "the chain is finished");
        assertTrue(find(entries(a), "d01").getClaimed(), "claiming everything pays the finished daily quests too");
        assertFalse(find(entries(a), "d03").getClaimed());
        assertFalse(quests.claim(a, "d03").getOk(), "5 bosses were not killed");
        assertFalse(quests.claim(a, "").getOk(), "nothing is left to claim");
    }

    @Test
    void countersStopAtTheTargetAndDailyQuestsStartAgainEveryDay() throws Exception {
        long a = newAccount();
        TODAY[0] = LocalDate.of(2026, 10, 10);
        quests.record(a, "BOSS_KILL", 4);
        assertEquals(4, find(entries(a), "d03").getProgress());
        quests.record(a, "BOSS_KILL", 4);
        assertEquals(5, find(entries(a), "d03").getProgress(), "never above the target");
        long cards0 = cards(a);
        assertTrue(quests.claim(a, "d03").getOk());
        assertEquals(cards0 + 5, cards(a));
        assertTrue(find(entries(a), "d03").getClaimed());
        assertFalse(quests.claim(a, "d03").getOk(), "once a day");
        quests.record(a, "BOSS_KILL", 1);
        assertEquals(5, find(entries(a), "d03").getProgress(), "a claimed daily quest does not count more today");

        TODAY[0] = LocalDate.of(2026, 10, 11);                      // a new day
        QuestEntry again = find(entries(a), "d03");
        assertEquals(0, again.getProgress());
        assertFalse(again.getClaimed());
        quests.record(a, "BOSS_KILL", 5);
        assertTrue(quests.claim(a, "d03").getOk());
        assertEquals(cards0 + 10, cards(a));
    }

    @Test
    void anotherAccountIsNotAffected() throws Exception {
        long a = newAccount(), b = newAccount();
        quests.record(a, "ATTENDANCE", 1);
        assertEquals(0, find(entries(b), "t01").getProgress());
    }
}
