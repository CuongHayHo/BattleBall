package com.battleball.server;

import com.battleball.proto.ChangePasswordError;
import com.battleball.proto.ChangePasswordRequest;
import com.battleball.proto.DisconnectReason;
import com.battleball.proto.Envelope;
import com.battleball.proto.ErrorCode;
import com.battleball.proto.Hello;
import com.battleball.proto.MailAction;
import com.battleball.proto.MailActionRequest;
import com.battleball.proto.MailListRequest;
import com.battleball.proto.NoticeListRequest;
import com.battleball.proto.LoginError;
import com.battleball.proto.LoginRequest;
import com.battleball.proto.LoginResult;
import com.battleball.proto.Ping;
import com.battleball.proto.RegisterError;
import com.battleball.proto.RegisterRequest;
import com.battleball.proto.RegisterResult;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.auth.AuthService;
import com.battleball.server.auth.PasswordHasher;
import com.battleball.server.db.Database;
import com.battleball.server.net.GameServer;
import com.battleball.server.net.TlsFactory;
import io.netty.handler.ssl.SslContext;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * End to end: real TLS connection, real Protobuf frames, real MariaDB (a throw-away database loaded with the migrations).
 * Runs only when BB_TEST_DB_URL (and optionally BB_TEST_DB_USER / BB_TEST_DB_PASSWORD) is set.
 */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class ProtocolIntegrationTest {
    private static Path keystore;
    private static TestDb testDb;
    private static ServerConfig config;
    private static Database db;
    private static ExecutorService dbExecutor;
    private static GameServer server;
    private static int port;
    private static AuthService authService;
    private static com.battleball.server.data.StaticDataStore staticData;
    private static com.battleball.server.game.PlayerActions actions;
    private static com.battleball.server.game.match.MatchManager matches;
    private static String manifestPath;
    private static byte[] packBytes;
    private static String packSha;

    @BeforeAll
    static void start() throws Exception {
        Path dir = Files.createTempDirectory("bbtest");
        keystore = dir.resolve("test.p12");
        String keytool = Path.of(System.getProperty("java.home"), "bin", "keytool").toString();
        Process p = new ProcessBuilder(keytool, "-genkeypair", "-alias", "bb", "-keyalg", "RSA", "-keysize", "2048", "-validity", "30",
                "-storetype", "PKCS12", "-keystore", keystore.toString(), "-storepass", "changeit", "-dname", "CN=localhost",
                "-ext", "SAN=dns:localhost,ip:127.0.0.1").redirectErrorStream(true).start();
        p.getInputStream().readAllBytes();
        assertEquals(0, p.waitFor(), "keytool failed");
        // assets served over the game connection: one real pack, one entry that points outside the folder, one entry whose file does not exist
        Path assets = dir.resolve("assets");
        Files.createDirectories(assets.resolve("packs"));
        packBytes = new byte[300_000];
        new java.util.Random(7).nextBytes(packBytes);
        Files.write(assets.resolve("packs").resolve("p1.zip"), packBytes);
        Files.writeString(dir.resolve("secret.txt"), "secret");
        String sha = java.util.HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(packBytes));
        Files.writeString(assets.resolve("manifest.json"), "{\"version\":\"v-test\",\"packs\":["
                + "{\"id\":\"p1\",\"scale\":3,\"url\":\"packs/p1.zip\",\"size\":1,\"sha256\":\"" + sha + "\"},"
                + "{\"id\":\"evil\",\"scale\":0,\"url\":\"../../secret.txt\",\"size\":1,\"sha256\":\"00\"},"
                + "{\"id\":\"gone\",\"scale\":0,\"url\":\"packs/gone.zip\",\"size\":1,\"sha256\":\"00\"}]}");
        manifestPath = assets.resolve("manifest.json").toString();
        packSha = sha;
        testDb = TestDb.create();
        config = testConfig(500, 1500);
        db = new Database(config);
        dbExecutor = Executors.newFixedThreadPool(4);
        authService = new AuthService(new AccountRepository(db), new PasswordHasher(4), config);
        SslContext ssl = TlsFactory.serverContext(keystore, "changeit");
        staticData = new com.battleball.server.data.StaticDataStore(new com.battleball.server.data.StaticDataLoader(db));
        actions = new com.battleball.server.game.PlayerActions(db, staticData, new com.battleball.server.game.EconomyService(db, staticData));
        matches = new com.battleball.server.game.match.MatchManager(db, staticData, actions, dbExecutor, 2, new java.security.SecureRandom());
        server = new GameServer(config, ssl, authService, dbExecutor, actions, matches);
        port = server.start();
    }

    private static ServerConfig testConfig(int maxConnections, int helloTimeoutMs) {
        return new ServerConfig(0, testDb.url, testDb.user, testDb.password, 4,
                keystore, "changeit", maxConnections, helloTimeoutMs, 60_000, 15_000, 20, 1 << 20, 4, 30, false, null, 0, "", manifestPath, "Test server");
    }

    @AfterAll
    static void stop() throws Exception {
        server.close();
        dbExecutor.shutdownNow();
        db.close();
        testDb.close();
    }

    // ---- helpers ----
    private static Envelope hello(int rid, int version) {
        return Envelope.newBuilder().setRequestId(rid).setHello(Hello.newBuilder().setProtocolVersion(version).setClientVersion("test").setPlatform("junit")).build();
    }

    private static Envelope register(int rid, String user, String pw) {
        return Envelope.newBuilder().setRequestId(rid).setRegisterRequest(RegisterRequest.newBuilder().setUsername(user).setPassword(pw)).build();
    }

    private static Envelope loginPw(int rid, String user, String pw) {
        return Envelope.newBuilder().setRequestId(rid).setLoginRequest(LoginRequest.newBuilder().setUsername(user).setPassword(pw).setDeviceInfo("junit")).build();
    }

    private static Envelope loginToken(int rid, String token) {
        return Envelope.newBuilder().setRequestId(rid).setLoginRequest(LoginRequest.newBuilder().setRefreshToken(token)).build();
    }

    /** Creates an account without the register message: the per-address registration limit (10 per hour) is shared by every test of this class. */
    private static void createAccount(String user, String password) throws Exception {
        new AccountRepository(db).createAccount(user, new PasswordHasher(4).hash(password));
    }

    private static String uniqueUser() {
        return "u" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
    }

    private TestClient connected() throws Exception {
        TestClient c = new TestClient(port);
        c.send(hello(1, ServerConfig.PROTOCOL_VERSION));
        assertTrue(c.expect(3000).hasHelloAck());
        return c;
    }

    private static long scalar(String sql, Object... params) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getLong(1);
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

    // ---- tests ----
    @Test
    void helloAckAndPing() throws Exception {
        try (TestClient c = new TestClient(port)) {
            c.send(hello(5, ServerConfig.PROTOCOL_VERSION));
            Envelope ack = c.expect(3000);
            assertEquals(5, ack.getRequestId());
            assertEquals(ServerConfig.PROTOCOL_VERSION, ack.getHelloAck().getProtocolVersion());
            assertEquals(15_000, ack.getHelloAck().getHeartbeatIntervalMs());
            assertEquals(20, ack.getHelloAck().getTickRateHz());
            assertEquals("Test server", ack.getHelloAck().getServerName());
            c.send(Envelope.newBuilder().setRequestId(6).setPing(Ping.newBuilder().setClientTimeMs(123)).build());
            Envelope pong = c.expect(3000);
            assertEquals(6, pong.getRequestId());
            assertEquals(123, pong.getPong().getClientTimeMs());
            assertTrue(pong.getPong().getServerTimeMs() > 0);
        }
    }

    @Test
    void assetsAreServedOverTheGameConnection() throws Exception {
        // before Hello nothing is served
        try (TestClient c = new TestClient(port)) {
            c.send(Envelope.newBuilder().setRequestId(1).setAssetManifestRequest(com.battleball.proto.AssetManifestRequest.getDefaultInstance()).build());
            assertEquals(ErrorCode.INVALID_STATE, c.expect(3000).getError().getCode());
        }
        try (TestClient c = connected()) {
            // HelloAck no longer carries a URL; it carries the version
            c.send(Envelope.newBuilder().setRequestId(2).setAssetManifestRequest(com.battleball.proto.AssetManifestRequest.getDefaultInstance()).build());
            Envelope m = c.expect(3000);
            assertEquals(2, m.getRequestId());
            assertEquals("v-test", m.getAssetManifestReply().getVersion());
            assertEquals(1, m.getAssetManifestReply().getPacksCount(), "only the pack inside the folder that exists is listed");
            var info = m.getAssetManifestReply().getPacks(0);
            assertEquals("p1", info.getId());
            assertEquals(3, info.getScale());
            assertEquals(packBytes.length, info.getSize(), "the size is that of the file, not the one written in the manifest");
            assertEquals(packSha, info.getSha256());
            // the whole pack, chunk by chunk (the default is 128 KiB, a bigger ask is cut to 256 KiB)
            java.io.ByteArrayOutputStream got = new java.io.ByteArrayOutputStream();
            int rid = 10;
            while (got.size() < packBytes.length) {
                c.send(Envelope.newBuilder().setRequestId(rid++).setAssetChunkRequest(com.battleball.proto.AssetChunkRequest.newBuilder()
                        .setPackId("p1").setOffset(got.size()).setMaxBytes(got.size() == 0 ? 0 : 10_000_000)).build());
                var chunk = c.expect(3000).getAssetChunk();
                assertEquals(got.size(), chunk.getOffset());
                assertEquals(packBytes.length, chunk.getTotal());
                assertTrue(chunk.getData().size() > 0 && chunk.getData().size() <= 256 * 1024);
                if (got.size() == 0) {
                    assertEquals(128 * 1024, chunk.getData().size());
                }
                got.write(chunk.getData().toByteArray());
            }
            assertTrue(java.util.Arrays.equals(packBytes, got.toByteArray()));
            // past the end: an empty chunk; unknown or escaping packs: refused
            c.send(Envelope.newBuilder().setRequestId(90).setAssetChunkRequest(com.battleball.proto.AssetChunkRequest.newBuilder().setPackId("p1").setOffset(packBytes.length + 5)).build());
            assertEquals(0, c.expect(3000).getAssetChunk().getData().size());
        }
        for (String bad : new String[] {"evil", "gone", "../secret.txt", ""}) {        // a connection is closed after 3 invalid messages: one each
            try (TestClient c = connected()) {
                c.send(Envelope.newBuilder().setRequestId(91).setAssetChunkRequest(com.battleball.proto.AssetChunkRequest.newBuilder().setPackId(bad)).build());
                assertEquals(ErrorCode.MALFORMED, c.expect(3000).getError().getCode(), bad);
            }
        }
    }

    @Test
    void unsupportedVersionIsRefusedAndClosed() throws Exception {
        try (TestClient c = new TestClient(port)) {
            c.send(hello(1, 99));
            Envelope e = c.expect(3000);
            assertEquals(ErrorCode.PROTOCOL_VERSION_UNSUPPORTED, e.getError().getCode());
            assertTrue(c.waitClosed(3000));
        }
    }

    @Test
    void loginBeforeHelloIsInvalidState() throws Exception {
        try (TestClient c = new TestClient(port)) {
            c.send(loginPw(1, "someone", "password1"));
            assertEquals(ErrorCode.INVALID_STATE, c.expect(3000).getError().getCode());
            // three violations close the connection
            c.send(loginPw(2, "someone", "password1"));
            c.expect(3000);
            c.send(loginPw(3, "someone", "password1"));
            c.expect(3000);
            assertEquals(DisconnectReason.PROTOCOL_VIOLATION, c.expect(3000).getDisconnectNotice().getReason());
            assertTrue(c.waitClosed(3000));
        }
    }

    @Test
    void registerCreatesAccountThreeCharactersWalletsAndStarterSkins() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            RegisterResult r = c.expect(5000).getRegisterResult();
            assertTrue(r.getOk(), "register should succeed: " + r.getError());
        }
        long accountId = scalar("SELECT account_id FROM account WHERE username = ?", user);
        assertEquals(3, scalar("SELECT COUNT(*) FROM game_character WHERE account_id = ?", accountId));
        assertEquals(3, scalar("SELECT COUNT(DISTINCT race) FROM game_character WHERE account_id = ?", accountId));
        assertEquals(1, scalar("SELECT COUNT(*) FROM account_wallet WHERE account_id = ?", accountId), "one wallet for the three characters");
        assertEquals(9, scalar("SELECT COUNT(*) FROM owned_skin WHERE account_id = ? AND source = 'DEFAULT'", accountId));
        assertEquals(3, scalar("SELECT COUNT(*) FROM character_equipment e JOIN game_character c USING (character_id) WHERE c.account_id = ? AND e.slot = 'SKIN'", accountId));
        String hash = "";
        try (Connection cn = db.connection(); PreparedStatement ps = cn.prepareStatement("SELECT password_hash FROM account WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                hash = rs.getString(1);
            }
        }
        assertTrue(hash.startsWith("$2a$") && !hash.contains("password123"), "the password must be stored as a bcrypt hash");
    }

    @Test
    void registerRejectsBadAndDuplicateInput() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(register(2, "ab", "password123"));
            assertEquals(RegisterError.USERNAME_INVALID, c.expect(5000).getRegisterResult().getError());
            c.send(register(3, "UPPER_case", "password123"));
            assertEquals(RegisterError.USERNAME_INVALID, c.expect(5000).getRegisterResult().getError());
            c.send(register(4, user, "short"));
            assertEquals(RegisterError.PASSWORD_INVALID, c.expect(5000).getRegisterResult().getError());
            c.send(register(5, user, "password123"));
            assertTrue(c.expect(5000).getRegisterResult().getOk());
            c.send(register(6, user, "password123"));
            assertEquals(RegisterError.USERNAME_TAKEN, c.expect(5000).getRegisterResult().getError());
        }
    }

    @Test
    void loginWithPasswordThenWithRefreshToken() throws Exception {
        String user = uniqueUser();
        String token;
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            assertTrue(c.expect(5000).getRegisterResult().getOk());
            c.send(loginPw(3, user, "wrong-password"));
            assertEquals(LoginError.AUTH_FAILED, c.expect(5000).getLoginResult().getError());
            c.send(loginPw(4, "nobody_" + user.substring(0, 6), "password123"));
            assertEquals(LoginError.AUTH_FAILED, c.expect(5000).getLoginResult().getError());   // unknown user: same answer as a wrong password
            c.send(loginPw(5, user, "password123"));
            LoginResult ok = c.expect(5000).getLoginResult();
            assertTrue(ok.getOk());
            assertEquals(user, ok.getUsername());
            assertTrue(ok.getAccountId() > 0);
            assertFalse(ok.getSessionId().isEmpty());
            assertFalse(ok.getRefreshToken().isEmpty());
            token = ok.getRefreshToken();
            c.send(Envelope.newBuilder().setRequestId(6).setPing(Ping.getDefaultInstance()).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.PONG, 3000).hasPong());
        }
        assertEquals(1, scalar("SELECT COUNT(*) FROM auth_token t JOIN account a USING (account_id) WHERE a.username = ?", user));
        assertEquals(0, scalar("SELECT COUNT(*) FROM auth_token WHERE token_hash = ?", token), "the raw token must not be stored");
        try (TestClient c = connected()) {
            c.send(loginToken(2, token));
            LoginResult viaToken = c.expect(5000).getLoginResult();
            assertTrue(viaToken.getOk());
            assertEquals(user, viaToken.getUsername());
            assertTrue(viaToken.getRefreshToken().isEmpty(), "a token login does not issue a new token");
        }
        try (TestClient c = connected()) {
            c.send(loginToken(2, "not-a-real-token"));
            assertEquals(LoginError.AUTH_FAILED, c.expect(5000).getLoginResult().getError());
        }
    }

    @Test
    void loginPushesThePlayerDataSnapshot() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            c.expect(5000);
            c.send(loginPw(3, user, "password123"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(user, snap.getUsername());
            assertEquals(3, snap.getCharactersCount());
            assertEquals(1819, snap.getPotara().getItemId(), "the earring is the Nro item 1819 (level 3)");
            assertEquals(15614, snap.getPotara().getIconId());
            assertEquals(1820, snap.getPotara().getShardItemId());
            assertEquals(15613, snap.getPotara().getShardIconId());
            assertEquals(100, snap.getPotara().getShardsNeeded());
            assertEquals(50, snap.getPotara().getMinFusionLevel());
            assertEquals(0, snap.getGold());
            assertEquals(9, snap.getSkinsCount(), "the three characters share the 9 starter skins");
            assertTrue(snap.getSkinsList().stream().allMatch(s -> !s.getName().isEmpty()), "every skin carries its name");
            for (var ch : snap.getCharactersList()) {
                assertEquals(1, ch.getLevel());
                assertEquals(100, ch.getExpToNext());      // coef 100 x level^2 at level 1
                assertTrue(ch.getDefaultCostumeId() > 0, "every race has a default costume to show when it has no skin of its own");
                assertTrue(ch.getBonusesList().stream().allMatch(l -> l.getSource().equals("TOTAL")), "bonus totals are labelled TOTAL");
                assertEquals(1, ch.getSkillsCount(), "every new character knows the basic punch");
                assertEquals(288_945, ch.getStats().getHpMax());
                assertEquals(13_065, ch.getStats().getAtk());
                assertTrue(ch.getEquipmentList().stream().anyMatch(e -> e.getSlot().equals("SKIN") && e.getCostumeId() > 0));
            }
            assertFalse(snap.getStaticVersionsMap().isEmpty());
            assertEquals(20, snap.getPotara().getDailyCap());
        }
    }

    @Test
    void changingThePasswordNeedsTheOldOneAndRevokesTheOldTokens() throws Exception {
        String user = uniqueUser();
        String oldToken;
        String newToken;
        createAccount(user, "password123");
        try (TestClient c = connected()) {
            c.send(loginPw(3, user, "password123"));
            oldToken = c.expect(5000).getLoginResult().getRefreshToken();
            c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000);
            c.send(Envelope.newBuilder().setRequestId(4).setChangePasswordRequest(ChangePasswordRequest.newBuilder().setOldPassword("not-the-password").setNewPassword("brand-new-pass")).build());
            assertEquals(ChangePasswordError.WRONG_OLD_PASSWORD, c.expectCase(Envelope.PayloadCase.CHANGE_PASSWORD_RESULT, 5000).getChangePasswordResult().getError());
            c.send(Envelope.newBuilder().setRequestId(5).setChangePasswordRequest(ChangePasswordRequest.newBuilder().setOldPassword("password123").setNewPassword("short")).build());
            assertEquals(ChangePasswordError.NEW_PASSWORD_INVALID, c.expectCase(Envelope.PayloadCase.CHANGE_PASSWORD_RESULT, 5000).getChangePasswordResult().getError());
            c.send(Envelope.newBuilder().setRequestId(6).setChangePasswordRequest(ChangePasswordRequest.newBuilder().setOldPassword("password123").setNewPassword("brand-new-pass")).build());
            var ok = c.expectCase(Envelope.PayloadCase.CHANGE_PASSWORD_RESULT, 5000).getChangePasswordResult();
            assertTrue(ok.getOk());
            newToken = ok.getRefreshToken();
            assertFalse(newToken.isEmpty());
        }
        try (TestClient c = connected()) {
            c.send(loginToken(2, oldToken));
            assertEquals(LoginError.TOKEN_REVOKED, c.expect(5000).getLoginResult().getError(), "the old tokens died with the old password");
        }
        try (TestClient c = connected()) {
            c.send(loginToken(2, newToken));
            assertTrue(c.expect(5000).getLoginResult().getOk());
        }
        try (TestClient c = connected()) {
            // (a failed password login would count against the per-address limit that the whole suite shares, so the old password is checked through the change itself)
            c.send(loginPw(2, user, "brand-new-pass"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            c.send(Envelope.newBuilder().setRequestId(3).setChangePasswordRequest(ChangePasswordRequest.newBuilder().setOldPassword("password123").setNewPassword("another-pass-1")).build());
            assertEquals(ChangePasswordError.WRONG_OLD_PASSWORD, c.expectCase(Envelope.PayloadCase.CHANGE_PASSWORD_RESULT, 5000).getChangePasswordResult().getError(),
                    "the old password no longer works");
        }
    }

    @Test
    void uronShopListsEverySkinOnSaleAndTheyCanBeBought() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(Envelope.newBuilder().setRequestId(2).setShopRequest(com.battleball.proto.ShopRequest.newBuilder().setNpcId("uron")).build());
            assertEquals(Envelope.PayloadCase.ERROR, c.expect(3000).getPayloadCase(), "a shop needs a login");
        }
        createAccount(user, "password123");
        long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
        update("UPDATE account_wallet SET gems = 100000 WHERE account_id = ?", account);
        long onSale = scalar("SELECT COUNT(*) FROM skin_template s JOIN skin_kind k USING (kind_key) WHERE s.enabled = 1 AND k.enabled = 1 AND k.sellable = 1 AND s.gem_price IS NOT NULL");
        try (TestClient c = connected()) {
            c.send(loginPw(3, user, "password123"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            c.send(Envelope.newBuilder().setRequestId(4).setShopRequest(com.battleball.proto.ShopRequest.newBuilder().setNpcId("uron")).build());
            var shop = c.expectCase(Envelope.PayloadCase.SHOP_CONTENT, 5000).getShopContent();
            assertEquals("Urôn", shop.getNpcName());
            int items = 0;
            com.battleball.proto.ShopItem cheapestAnyRace = null;
            for (var tab : shop.getTabsList()) {
                assertTrue(tab.getItemsCount() > 0, "empty tabs are not sent");
                int last = 0;
                for (var item : tab.getItemsList()) {
                    items++;
                    assertTrue(item.getGemPrice() >= last, "sorted by price");
                    last = item.getGemPrice();
                    assertTrue(item.getSkin().getCostumeId() > 0 && !item.getSkin().getName().isEmpty());
                    if (tab.getName().equals("Mọi tộc") && cheapestAnyRace == null && item.getSkin().getFusionRace().isEmpty()) {
                        cheapestAnyRace = item;
                    }
                }
            }
            assertEquals(onSale, items, "Urôn sells every skin that can be sold, each in exactly one tab");
            assertTrue(cheapestAnyRace != null);
            // buy one with the character of the account
            c.send(Envelope.newBuilder().setRequestId(5).setBuySkinRequest(com.battleball.proto.BuySkinRequest.newBuilder()
                    .setCharacterId(snap.getCharacters(0).getCharacterId()).setCostumeId(cheapestAnyRace.getSkin().getCostumeId())).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            var after = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(100000 - cheapestAnyRace.getGemPrice(), after.getGems());
            boolean owned = false;
            for (var sk : after.getSkinsList()) owned |= sk.getCostumeId() == cheapestAnyRace.getSkin().getCostumeId();
            assertTrue(owned, "the skin is in the bag now");
            // an NPC that sells nothing
            c.send(Envelope.newBuilder().setRequestId(6).setShopRequest(com.battleball.proto.ShopRequest.newBuilder().setNpcId("elder")).build());
            var none = c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult();
            assertFalse(none.getOk());
            assertEquals(com.battleball.proto.ActionError.NOT_FOUND, none.getError());
        }
    }

    @Test
    void bunmaSellsGearAndStonesAndChecksWhatIsOnSale() throws Exception {
        String user = uniqueUser();
        createAccount(user, "password123");
        long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
        update("UPDATE account_wallet SET gold = 1000000, gems = 1000 WHERE account_id = ?", account);
        try (TestClient c = connected()) {
            c.send(loginPw(3, user, "password123"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000);
            c.send(Envelope.newBuilder().setRequestId(4).setShopRequest(com.battleball.proto.ShopRequest.newBuilder().setNpcId("bunma")).build());
            var shop = c.expectCase(Envelope.PayloadCase.SHOP_CONTENT, 5000).getShopContent();
            assertEquals("Bunma", shop.getNpcName());
            assertEquals(562, shop.getPortraitIcon());
            assertEquals(5, shop.getTabsCount(), "three sets, the exchange tab and the stones");
            assertEquals("Thần Linh", shop.getTabs(0).getName());
            assertEquals("Hủy Diệt", shop.getTabs(1).getName());
            assertEquals("Thiên Sứ", shop.getTabs(2).getName());
            assertEquals("Đổi mảnh", shop.getTabs(3).getName());
            assertEquals("Đá", shop.getTabs(4).getName());
            for (int set = 0; set < 3; set++) {
                assertEquals(15, shop.getTabs(set).getItemsCount(), "five pieces of the set for each of the three planets");
                for (var piece : shop.getTabs(set).getItemsList()) {
                    assertEquals("GEAR", piece.getKind());
                    assertTrue(piece.getName().contains(shop.getTabs(set).getName()), piece.getName());
                    assertEquals(new long[] {6000, 9000, 12000}[set], piece.getGoldPrice(), "10 fragments of 0.6 / 0.9 / 1.2 wins at level 1");
                    assertTrue(piece.getIconId() > 0 && !piece.getDescription().isEmpty());
                }
            }
            assertEquals("Áo Thần Linh Trái Đất", shop.getTabs(0).getItems(0).getName());
            assertEquals("Áo Thần Linh Namếc", shop.getTabs(0).getItems(5).getName());
            assertEquals("Áo Thần Linh Xayda", shop.getTabs(0).getItems(10).getName());
            assertEquals(shop.getTabs(0).getItems(0).getGoldPrice(), shop.getTabs(0).getItems(5).getGoldPrice(), "a piece costs the same for every planet");
            java.util.Set<Integer> pictures = new java.util.HashSet<>();
            for (var piece : shop.getTabs(2).getItemsList()) pictures.add(piece.getIconId());
            assertTrue(pictures.size() >= 12, "the planets show different pictures: " + pictures);
            assertEquals(1, shop.getTabs(0).getItems(0).getMinLevel());
            assertEquals(30, shop.getTabs(1).getItems(0).getMinLevel());
            assertEquals(60, shop.getTabs(2).getItems(0).getMinLevel());
            var exchange = shop.getTabs(3);
            assertEquals(45, exchange.getItemsCount(), "every piece of every set of every planet");
            assertEquals("EXCHANGE", exchange.getItems(0).getKind());
            assertEquals(10, exchange.getItems(0).getFragmentCost());
            assertEquals(101, exchange.getItems(0).getFragmentTemplateId());
            assertEquals(0, exchange.getItems(0).getGoldPrice());
            assertEquals(3, shop.getTabs(4).getItemsCount(), "the upgrade stone, the stat stone and the crystal stone");
            var crystal = shop.getTabs(4).getItems(2);
            assertEquals(203, crystal.getTemplateId());
            assertEquals(500, crystal.getGemPrice());
            assertEquals(0, crystal.getGoldPrice());
            assertEquals(1500, shop.getTabs(4).getItems(0).getGoldPrice(), "upgrade stone: 1.5 wins");
            // buy a piece of gear: 6.000 gold, one piece in the bag
            c.send(Envelope.newBuilder().setRequestId(5).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("GEAR").setTemplateId(1)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            var afterGear = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(994_000, afterGear.getGold());
            assertEquals(1, afterGear.getBagCount());
            // a piece of the second set needs level 30: refused before anything is spent
            c.send(Envelope.newBuilder().setRequestId(10).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("GEAR").setTemplateId(6)).build());
            assertEquals(com.battleball.proto.ActionError.LEVEL_TOO_LOW, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            // exchange: without the fragments it fails; with 10 fragments the piece comes and the fragments go
            c.send(Envelope.newBuilder().setRequestId(11).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("EXCHANGE").setTemplateId(2)).build());
            assertEquals(com.battleball.proto.ActionError.NOT_ENOUGH_MATERIAL, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            update("INSERT INTO material_stack (account_id, template_id, quantity) VALUES (?, 102, 12)", account);
            c.send(Envelope.newBuilder().setRequestId(12).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("EXCHANGE").setTemplateId(2)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            var afterExchange = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(2, afterExchange.getBagCount());
            assertEquals(994_000, afterExchange.getGold(), "an exchange costs no gold");
            long left = 0;
            for (var m : afterExchange.getMaterialsList()) if (m.getTemplateId() == 102) left = m.getQuantity();
            assertEquals(2, left, "12 fragments minus the 10 of the piece");
            // the same fragments make the piece of any planet: 8 more, then the Namếc pants (template 17) are exchanged for them
            update("UPDATE material_stack SET quantity = quantity + 8 WHERE account_id = ? AND template_id = 102", account);
            c.send(Envelope.newBuilder().setRequestId(15).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("EXCHANGE").setTemplateId(17)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            assertEquals(3, c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot().getBagCount());
            // buy stones: 3 upgrade stones for 4.500 gold, then a crystal stone for 500 gems
            c.send(Envelope.newBuilder().setRequestId(6).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("MATERIAL").setTemplateId(201).setQuantity(3)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            assertEquals(989_500, c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot().getGold());
            c.send(Envelope.newBuilder().setRequestId(7).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("MATERIAL").setTemplateId(203).setQuantity(1)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            var afterCrystal = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(500, afterCrystal.getGems());
            long stones = 0, crystals = 0;
            for (var m : afterCrystal.getMaterialsList()) {
                if (m.getTemplateId() == 201) stones = m.getQuantity();
                if (m.getTemplateId() == 203) crystals = m.getQuantity();
            }
            assertEquals(3, stones);
            assertEquals(1, crystals);
            // things the shop does not sell are refused before anything is spent
            for (var bad : new Object[][] {{"bunma", "MATERIAL", 301}, {"bunma", "MATERIAL", 105}, {"uron", "GEAR", 1}, {"nobody", "GEAR", 1}, {"bunma", "EXCHANGE", 999}}) {
                c.send(Envelope.newBuilder().setRequestId(8).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder()
                        .setNpcId((String) bad[0]).setKind((String) bad[1]).setTemplateId((Integer) bad[2]).setQuantity(1)).build());
                var refused = c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult();
                assertFalse(refused.getOk(), bad[0] + " " + bad[1] + " " + bad[2]);
                assertEquals(com.battleball.proto.ActionError.NOT_FOUND, refused.getError());
            }
            update("UPDATE account_wallet SET gold = 100 WHERE account_id = ?", account);
            c.send(Envelope.newBuilder().setRequestId(9).setShopBuyRequest(com.battleball.proto.ShopBuyRequest.newBuilder().setNpcId("bunma").setKind("GEAR").setTemplateId(2)).build());
            assertEquals(com.battleball.proto.ActionError.NOT_ENOUGH_GOLD, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
        }
    }

    @Test
    void gearIsDescribedForTheClientInTheBagAndOnTheCharacter() throws Exception {
        String user = uniqueUser();
        createAccount(user, "password123");
        long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
        update("INSERT INTO item_instance (account_id, template_id, slot, upgrade_percent) VALUES (?, 1, 'ARMOR', 10)", account);       // Áo Thần Linh Trái Đất, +10%
        update("INSERT INTO item_instance (account_id, template_id, slot) VALUES (?, 17, 'PANTS')", account);                          // Quần Thần Linh Namếc
        long armor = scalar("SELECT item_id FROM item_instance WHERE account_id = ? AND template_id = 1", account);
        try (TestClient c = connected()) {
            c.send(loginPw(3, user, "password123"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            long earth = 0;
            for (var ch : snap.getCharactersList()) if (ch.getRace().equals("EARTH")) earth = ch.getCharacterId();
            c.send(Envelope.newBuilder().setRequestId(4).setInventoryRequest(com.battleball.proto.InventoryRequest.newBuilder().setCharacterId(earth)).build());
            var page = c.expectCase(Envelope.PayloadCase.INVENTORY_PAGE, 5000).getInventoryPage();
            assertEquals(2, page.getItemsCount());
            var piece = page.getItems(0);
            assertEquals("Áo Thần Linh Trái Đất", piece.getName());
            assertEquals("EARTH", piece.getRace());
            assertEquals(1, piece.getTier());
            assertEquals(1, piece.getMinLevel());
            assertTrue(piece.getIconId() > 0);
            assertEquals("FLAT_ARMOR", piece.getMainStat().getStatKey());
            assertEquals(2750.0, piece.getMainStat().getValue(), 0.001, "2.500 base with +10% upgrade");
            assertTrue(piece.getMainStat().getText().contains("2750"), piece.getMainStat().getText());
            assertEquals("NAMEC", page.getItems(1).getRace());
            // worn: the piece moves to the character in the snapshot with the same description, and the stats count it
            long armorBefore = 0;
            for (var ch : snap.getCharactersList()) if (ch.getCharacterId() == earth) armorBefore = ch.getStats().getArmor();
            c.send(Envelope.newBuilder().setRequestId(5).setEquipItemRequest(com.battleball.proto.EquipItemRequest.newBuilder().setCharacterId(earth).setItemId(armor)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            var after = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            for (var ch : after.getCharactersList()) {
                if (ch.getCharacterId() != earth) continue;
                assertEquals(1, ch.getEquippedItemsCount());
                assertEquals("Áo Thần Linh Trái Đất", ch.getEquippedItems(0).getName());
                assertTrue(ch.getEquippedItems(0).getEquipped());
                assertEquals(armorBefore + 2750, ch.getStats().getArmor(), "the armor of the piece counts in the final stats");
            }
            assertEquals(1, after.getBagCount());
        }
    }

    @Test
    void mailboxAndNoticesOverTheWire() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(Envelope.newBuilder().setRequestId(2).setMailListRequest(MailListRequest.getDefaultInstance()).build());
            assertEquals(Envelope.PayloadCase.ERROR, c.expect(3000).getPayloadCase(), "the mailbox needs a login");
        }
        update("DELETE FROM notice");
        update("INSERT INTO notice (title, body, created_by) VALUES ('Bảo trì', 'Tối nay 22h', 'test')");
        createAccount(user, "password123");
        try (TestClient c = connected()) {
            long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
            update("INSERT INTO mail (account_id, title, body, sent_by) VALUES (?, 'Quà tặng', 'Chúc vui', 'test')", account);
            long mailId = scalar("SELECT MAX(mail_id) FROM mail WHERE account_id = ?", account);
            update("INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) VALUES (?, 1, 'GEMS', 0, 50)", mailId);
            c.send(loginPw(3, user, "password123"));
            assertTrue(c.expect(5000).getLoginResult().getOk());
            assertEquals(1, c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot().getUnreadMail());
            c.send(Envelope.newBuilder().setRequestId(4).setMailListRequest(MailListRequest.getDefaultInstance()).build());
            var list = c.expectCase(Envelope.PayloadCase.MAIL_LIST, 5000).getMailList();
            assertEquals(1, list.getMailsCount());
            assertEquals("Quà tặng", list.getMails(0).getTitle());
            c.send(Envelope.newBuilder().setRequestId(5).setNoticeListRequest(NoticeListRequest.getDefaultInstance()).build());
            assertEquals("Bảo trì", c.expectCase(Envelope.PayloadCase.NOTICE_LIST, 5000).getNoticeList().getNotices(0).getTitle());
            c.send(Envelope.newBuilder().setRequestId(6).setMailActionRequest(MailActionRequest.newBuilder().setMailId(mailId).setAction(MailAction.MAIL_CLAIM)).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getOk());
            assertTrue(c.expectCase(Envelope.PayloadCase.MAIL_LIST, 5000).getMailList().getMails(0).getClaimed());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(50, snap.getGems());
            assertEquals(0, snap.getUnreadMail());
        }
    }

    @Test
    void revokedExpiredAndBannedAccounts() throws Exception {
        String user = uniqueUser();
        String token;
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            c.expect(5000);
            c.send(loginPw(3, user, "password123"));
            token = c.expect(5000).getLoginResult().getRefreshToken();
        }
        update("UPDATE auth_token t JOIN account a USING (account_id) SET t.expires_at = NOW() - INTERVAL 1 DAY WHERE a.username = ?", user);
        try (TestClient c = connected()) {
            c.send(loginToken(2, token));
            assertEquals(LoginError.TOKEN_EXPIRED, c.expect(5000).getLoginResult().getError());
        }
        update("UPDATE auth_token t JOIN account a USING (account_id) SET t.expires_at = NOW() + INTERVAL 1 DAY, t.revoked_at = NOW() WHERE a.username = ?", user);
        try (TestClient c = connected()) {
            c.send(loginToken(2, token));
            assertEquals(LoginError.TOKEN_REVOKED, c.expect(5000).getLoginResult().getError());
        }
        update("UPDATE account SET banned = 1 WHERE username = ?", user);
        try (TestClient c = connected()) {
            c.send(loginPw(2, user, "password123"));
            assertEquals(LoginError.ACCOUNT_BANNED, c.expect(5000).getLoginResult().getError());
        }
    }

    @Test
    void tokenUseSlidesTheExpiry() throws Exception {
        String user = uniqueUser();
        String token;
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            c.expect(5000);
            c.send(loginPw(3, user, "password123"));
            token = c.expect(5000).getLoginResult().getRefreshToken();
        }
        update("UPDATE auth_token t JOIN account a USING (account_id) SET t.expires_at = NOW() + INTERVAL 2 DAY WHERE a.username = ?", user);
        try (TestClient c = connected()) {
            c.send(loginToken(2, token));
            assertTrue(c.expect(5000).getLoginResult().getOk());
        }
        long days = scalar("SELECT DATEDIFF(t.expires_at, NOW()) FROM auth_token t JOIN account a USING (account_id) WHERE a.username = ?", user);
        assertTrue(days >= 29, "expiry should be pushed back to about 30 days, was " + days);
    }

    @Test
    void secondLoginKicksTheFirst() throws Exception {
        String user = uniqueUser();
        try (TestClient first = connected(); TestClient second = connected()) {
            first.send(register(2, user, "password123"));
            first.expect(5000);
            first.send(loginPw(3, user, "password123"));
            assertTrue(first.expect(5000).getLoginResult().getOk());
            second.send(loginPw(2, user, "password123"));
            assertTrue(second.expect(5000).getLoginResult().getOk());
            Envelope kick = first.expectCase(Envelope.PayloadCase.DISCONNECT_NOTICE, 3000);
            assertEquals(DisconnectReason.LOGGED_IN_ELSEWHERE, kick.getDisconnectNotice().getReason());
            assertTrue(first.waitClosed(3000));
            second.send(Envelope.newBuilder().setRequestId(9).setPing(Ping.getDefaultInstance()).build());
            assertTrue(second.expectCase(Envelope.PayloadCase.PONG, 3000).hasPong(), "the new session must keep working");
        }
    }

    @Test
    void repeatedWrongPasswordsAreRateLimited() throws Exception {
        String user = uniqueUser();
        try (TestClient c = connected()) {
            c.send(register(2, user, "password123"));
            c.expect(5000);
            for (int i = 0; i < 8; i++) {
                c.send(loginPw(10 + i, user, "wrong-pass-" + i));
                assertEquals(LoginError.AUTH_FAILED, c.expect(5000).getLoginResult().getError());
            }
            c.send(loginPw(30, user, "password123"));   // even the right password is refused while limited
            assertEquals(LoginError.AUTH_RATE_LIMITED, c.expect(5000).getLoginResult().getError());
        }
    }

    @Test
    void silentConnectionIsClosedAfterHelloTimeout() throws Exception {
        try (TestClient c = new TestClient(port)) {
            Envelope e = c.expect(5000);
            assertEquals(DisconnectReason.TIMEOUT, e.getDisconnectNotice().getReason());
            assertTrue(c.waitClosed(3000));
        }
    }

    @Test
    void loginWithoutAnyCredentialFails() throws Exception {
        try (TestClient c = connected()) {
            c.send(Envelope.newBuilder().setRequestId(1).setLoginRequest(LoginRequest.getDefaultInstance()).build());   // no credential at all
            LoginResult r = c.expect(5000).getLoginResult();
            assertFalse(r.getOk());
            assertNotEquals(LoginError.LOGIN_ERROR_NONE, r.getError());
        }
    }

    @Test
    void serverFullAtTheConnectionLimit() throws Exception {
        ServerConfig small = testConfig(1, 5000);
        try (GameServer s = new GameServer(small, TlsFactory.serverContext(keystore, "changeit"), authService, dbExecutor, actions, matches)) {
            int p = s.start();
            try (TestClient a = new TestClient(p); TestClient b = new TestClient(p)) {
                a.send(hello(1, ServerConfig.PROTOCOL_VERSION));
                assertTrue(a.expect(3000).hasHelloAck());
                Envelope full = b.expect(3000);
                assertEquals(DisconnectReason.SERVER_FULL, full.getDisconnectNotice().getReason());
                assertTrue(b.waitClosed(3000));
            }
        }
    }
}
