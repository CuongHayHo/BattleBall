package com.battleball.server;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.MailInfo;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.MailService;
import com.battleball.server.game.PlayerRepository;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The mailbox and the notices against a real database: a gift is paid once, all or nothing, into the shared inventory. */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class MailServiceTest {
    private static TestDb testDb;
    private static Database db;
    private static StaticDataStore data;
    private static AccountRepository accounts;
    private static MailService mail;

    @BeforeAll
    static void start() throws Exception {
        testDb = TestDb.create();
        db = new Database(testDb.url, testDb.user, testDb.password, 4);
        data = new StaticDataStore(new StaticDataLoader(db));
        accounts = new AccountRepository(db);
        mail = new MailService(db, data);
    }

    @AfterAll
    static void stop() throws Exception {
        db.close();
        testDb.close();
    }

    private static long newAccount() throws Exception {
        String user = "m" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
        accounts.createAccount(user, "$2a$04$abcdefghijklmnopqrstuuabcdefghijklmnopqrstuvwxyz01234");
        return scalar("SELECT account_id FROM account WHERE username = ?", user);
    }

    /** A mail with attachments {kind, ref, quantity}. */
    private static long send(long account, String title, Object[]... attachments) throws Exception {
        update("INSERT INTO mail (account_id, title, body, sent_by) VALUES (?,?,?, 'test')", account, title, "body of " + title);
        long id = scalar("SELECT MAX(mail_id) FROM mail WHERE account_id = ?", account);
        int line = 1;
        for (Object[] a : attachments) {
            update("INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) VALUES (?,?,?,?,?)", id, line++, a[0], a[1], a[2]);
        }
        return id;
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

    private static PlayerRepository.CharacterData character(long account) throws Exception {
        try (Connection c = db.connection()) {
            return PlayerRepository.load(c, account).characters().get(0);
        }
    }

    private static void assertError(ActionError expected, ActionResult r) {
        assertFalse(r.getOk(), "should have been refused");
        assertEquals(expected, r.getError(), r.getMessage());
    }

    @Test
    void aGiftIsPaidOnceIntoTheSharedInventory() throws Exception {
        long a = newAccount();
        int skin = data.get().skins().values().stream().filter(s -> s.kindKey().equals("FUSION_FORM")).findFirst().orElseThrow().id();
        long id = send(a, "Quà thử", new Object[]{"GOLD", 0, 5000}, new Object[]{"GEMS", 0, 30}, new Object[]{"SKIN", skin, 1},
                new Object[]{"MATERIAL", 101, 12}, new Object[]{"ITEM", 3, 2}, new Object[]{"EARRING", 0, 1});
        assertEquals(1, mail.list(a).getMailsCount());
        MailInfo info = mail.list(a).getMails(0);
        assertFalse(info.getClaimed());
        assertFalse(info.getRead());
        assertEquals(6, info.getAttachmentsCount());
        assertEquals("Vàng", info.getAttachments(0).getName());
        assertEquals(1, unread(a), "an unread mail with a gift counts for the badge");

        assertTrue(mail.read(a, id).getOk());
        assertEquals(1, unread(a), "read but the gift is not taken: still counted");

        assertTrue(mail.claim(a, id).getOk());
        var ch = character(a);
        assertEquals(5000, ch.gold());
        assertEquals(30, ch.gems());
        assertTrue(ch.ownedSkins().contains(skin));
        assertEquals(12, ch.materials().get(101));
        assertEquals(2, ch.bagCount());
        assertEquals(1, scalar("SELECT potara_owned FROM account WHERE account_id = ?", a));
        assertEquals(0, unread(a));
        assertTrue(mail.list(a).getMails(0).getClaimed());

        assertError(ActionError.ALREADY_OWNED, mail.claim(a, id));                      // never twice
        assertEquals(5000, character(a).gold());
        assertEquals(2, scalar("SELECT COUNT(*) FROM currency_ledger WHERE reason = 'MAIL' AND ref = ?", "mail:" + id));
    }

    @Test
    void aFullBagRefusesTheWholeGiftAndNothingIsPaid() throws Exception {
        long a = newAccount();
        update("UPDATE balance_config SET value_json = '{\"bag_limit\":1,\"bag_warn\":1,\"bag_page_size\":100,\"revives_per_match\":2}' WHERE config_key = 'inventory_rules'");
        update("UPDATE static_data_version SET version = version + 1 WHERE domain = 'BALANCE'");
        data.refreshIfChanged();
        try {
            long id = send(a, "Hai món", new Object[]{"GOLD", 0, 777}, new Object[]{"ITEM", 3, 2});
            assertError(ActionError.BAG_FULL, mail.claim(a, id));
            assertEquals(0, character(a).gold(), "all or nothing: the gold line was not paid either");
            assertFalse(mail.list(a).getMails(0).getClaimed());
        } finally {
            update("UPDATE balance_config SET value_json = '{\"bag_limit\":2000,\"bag_warn\":1800,\"bag_page_size\":100,\"revives_per_match\":2}' WHERE config_key = 'inventory_rules'");
            update("UPDATE static_data_version SET version = version + 1 WHERE domain = 'BALANCE'");
            data.refreshIfChanged();
        }
    }

    @Test
    void shardsAreCappedAndMailsOfOtherAccountsAreInvisible() throws Exception {
        long a = newAccount();
        long b = newAccount();
        long id = send(a, "Mảnh", new Object[]{"MATERIAL", 1820, 150});
        assertError(ActionError.NOT_FOUND, mail.claim(b, id));                          // someone else's mail
        assertEquals(0, mail.list(b).getMailsCount());
        assertTrue(mail.claim(a, id).getOk());
        assertEquals(100, scalar("SELECT potara_fragments FROM account WHERE account_id = ?", a), "an account never holds more than the 100 shards of the earring");
    }

    @Test
    void readDeleteAndClaimAll() throws Exception {
        long a = newAccount();
        long plain = send(a, "Chỉ chữ");
        long gift1 = send(a, "Quà 1", new Object[]{"GOLD", 0, 100});
        long gift2 = send(a, "Quà 2", new Object[]{"GEMS", 0, 5});
        assertTrue(mail.list(a).getMails(2).getClaimed(), "a mail without a gift counts as taken");
        assertError(ActionError.DISABLED, mail.delete(a, gift1));                       // the gift is not taken yet: kept
        assertTrue(mail.claimAll(a).getOk());
        assertEquals(100, character(a).gold());
        assertEquals(5, character(a).gems());
        assertError(ActionError.NOT_FOUND, mail.claimAll(a));                           // nothing left to take
        assertTrue(mail.delete(a, gift1).getOk());
        assertTrue(mail.delete(a, plain).getOk());
        assertEquals(1, mail.list(a).getMailsCount());
        assertEquals(gift2, mail.list(a).getMails(0).getMailId());
        assertError(ActionError.NOT_FOUND, mail.delete(a, plain));
    }

    @Test
    void onlyEnabledAndCurrentNoticesAreListedNewestFirst() throws Exception {
        update("DELETE FROM notice");
        update("INSERT INTO notice (title, body, created_by) VALUES ('Cũ', 'a', 'test')");
        update("INSERT INTO notice (title, body, created_by) VALUES ('Mới', 'b', 'test')");
        update("INSERT INTO notice (title, body, enabled, created_by) VALUES ('Tắt', 'c', 0, 'test')");
        update("INSERT INTO notice (title, body, expires_at, created_by) VALUES ('Hết hạn', 'd', NOW() - INTERVAL 1 DAY, 'test')");
        var list = mail.notices();
        assertEquals(2, list.getNoticesCount());
        assertEquals("Mới", list.getNotices(0).getTitle());
        assertEquals("Cũ", list.getNotices(1).getTitle());
    }

    private static int unread(long account) throws Exception {
        try (Connection c = db.connection()) {
            return MailService.unread(c, account);
        }
    }
}
