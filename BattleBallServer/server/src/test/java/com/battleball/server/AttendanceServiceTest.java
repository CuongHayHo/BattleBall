package com.battleball.server;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.AttendanceInfo;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.AttendanceService;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.time.LocalDate;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The daily check-in of the village elder (docs 15.25) against a real database: one claim a day, the table of the month, a new month starts again. */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class AttendanceServiceTest {
    private static TestDb testDb;
    private static Database db;
    private static AccountRepository accounts;
    private static AttendanceService attendance;
    private static final LocalDate[] TODAY = {LocalDate.of(2026, 10, 10)};

    @BeforeAll
    static void start() throws Exception {
        testDb = TestDb.create();
        db = new Database(testDb.url, testDb.user, testDb.password, 4);
        accounts = new AccountRepository(db);
        attendance = new AttendanceService(db, new StaticDataStore(new StaticDataLoader(db)), () -> TODAY[0]);
    }

    @AfterAll
    static void stop() throws Exception {
        db.close();
        testDb.close();
    }

    private static long newAccount() throws Exception {
        String user = "a" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
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

    private static long gold(long account) throws Exception {
        return scalar("SELECT gold FROM account_wallet WHERE account_id = ?", account);
    }

    private static long gems(long account) throws Exception {
        return scalar("SELECT gems FROM account_wallet WHERE account_id = ?", account);
    }

    private static long material(long account, int template) throws Exception {
        return scalar("SELECT COALESCE(SUM(quantity), 0) FROM material_stack WHERE account_id = ? AND template_id = ?", account, template);
    }

    @Test
    void theTableShowsTheMonthWithTheRewardOfEveryDay() throws Exception {
        long a = newAccount();
        TODAY[0] = LocalDate.of(2026, 10, 10);
        AttendanceInfo info = attendance.info(a);
        assertEquals(2026, info.getYear());
        assertEquals(10, info.getMonth());
        assertEquals(10, info.getToday());
        assertEquals(31, info.getDaysInMonth());
        assertEquals(31, info.getDaysCount());
        assertFalse(info.getClaimedToday());
        assertEquals(250, info.getDays(0).getGold(), "a quarter of a win at level 1 (a win gives 1000 x level)");
        assertEquals(3, info.getDays(0).getGems());
        assertEquals(8, info.getDays(6).getGems(), "day 7 adds 5 gems to the 3 of every day");
        assertEquals(1, info.getDays(9).getItemsCount(), "day 10 gives a free item");
        assertEquals(205, info.getDays(9).getItems(0).getTemplateId());
        assertEquals(0, info.getDays(1).getItemsCount(), "day 2 gives none");
        TODAY[0] = LocalDate.of(2027, 2, 3);
        assertEquals(28, attendance.info(a).getDaysInMonth(), "February");
    }

    @Test
    void anAccountChecksInOnceADayAndGetsTheGoldTheGemsAndTheItemsOfTheDay() throws Exception {
        long a = newAccount();
        long other = newAccount();
        TODAY[0] = LocalDate.of(2026, 10, 10);
        long gold0 = gold(a), gems0 = gems(a), stones0 = material(a, 205);
        ActionResult r = attendance.claim(a);
        assertTrue(r.getOk());
        assertEquals(gold0 + 250, gold(a));
        assertEquals(gems0 + 3, gems(a));
        assertEquals(stones0 + 1, material(a, 205), "the free item of day 10");
        assertTrue(attendance.info(a).getClaimedToday());
        assertTrue(attendance.info(a).getDays(9).getClaimed());

        ActionResult again = attendance.claim(a);
        assertFalse(again.getOk());
        assertEquals(ActionError.ALREADY_OWNED, again.getError());
        assertEquals(gold0 + 250, gold(a), "nothing is paid twice");
        assertFalse(attendance.info(other).getClaimedToday(), "another account is not affected");

        TODAY[0] = LocalDate.of(2026, 10, 11);
        assertFalse(attendance.info(a).getClaimedToday(), "a new day");
        assertTrue(attendance.claim(a).getOk());
        assertEquals(gold0 + 500, gold(a));
        AttendanceInfo info = attendance.info(a);
        assertEquals(2, info.getClaimedCount());
        assertTrue(info.getDays(9).getClaimed() && info.getDays(10).getClaimed() && !info.getDays(11).getClaimed());

        TODAY[0] = LocalDate.of(2026, 11, 1);                                                 // a new month starts with an empty table
        info = attendance.info(a);
        assertEquals(30, info.getDaysInMonth());
        assertEquals(0, info.getClaimedCount());
        long upgrade0 = material(a, 201);
        assertTrue(attendance.claim(a).getOk());
        assertEquals(upgrade0 + 1, material(a, 201), "day 1 gives an upgrade stone");
    }
}
