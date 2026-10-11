package com.battleball.server;

import com.battleball.proto.ActionError;
import com.battleball.proto.DisconnectReason;
import com.battleball.proto.EntityKind;
import com.battleball.proto.Envelope;
import com.battleball.proto.Hello;
import com.battleball.proto.LeaveMatchRequest;
import com.battleball.proto.LoginRequest;
import com.battleball.proto.MatchEnded;
import com.battleball.proto.MatchFrame;
import com.battleball.proto.MatchResult;
import com.battleball.proto.MatchStarted;
import com.battleball.proto.PlayerStateReport;
import com.battleball.proto.RegisterRequest;
import com.battleball.proto.ResumeMatchRequest;
import com.battleball.proto.StartMatchRequest;
import com.battleball.proto.StartTrainingRequest;
import com.battleball.proto.TrainingSpot;
import com.battleball.proto.UseSkillRequest;
import com.battleball.server.auth.AccountRepository;
import com.battleball.server.auth.AuthService;
import com.battleball.server.auth.PasswordHasher;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.EconomyService;
import com.battleball.server.game.PlayerActions;
import com.battleball.server.game.match.MatchManager;
import com.battleball.server.net.GameServer;
import com.battleball.server.net.TlsFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.Random;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** A whole match over TLS: login, start, frames, play, the end with rewards; losing the connection and coming back. The simulation runs 25 times faster than real time. */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class MatchIntegrationTest {
    private static TestDb testDb;
    private static Database db;
    private static ExecutorService dbExecutor;
    private static GameServer server;
    private static MatchManager matches;
    private static int port;

    @BeforeAll
    static void start() throws Exception {
        Path dir = Files.createTempDirectory("bbmatch");
        Path keystore = dir.resolve("t.p12");
        Process p = new ProcessBuilder(Path.of(System.getProperty("java.home"), "bin", "keytool").toString(), "-genkeypair", "-alias", "bb", "-keyalg", "RSA",
                "-keysize", "2048", "-validity", "30", "-storetype", "PKCS12", "-keystore", keystore.toString(), "-storepass", "changeit", "-dname", "CN=localhost",
                "-ext", "SAN=dns:localhost,ip:127.0.0.1").redirectErrorStream(true).start();
        p.getInputStream().readAllBytes();
        assertEquals(0, p.waitFor());
        testDb = TestDb.create();
        // an easier world so that a test squad of level 30 wins quickly: weaker early bosses and a short pause limit
        try (Database admin = new Database(testDb.url, testDb.user, testDb.password, 1); Connection c = admin.connection(); var st = c.createStatement()) {
            st.execute("UPDATE monster_stat_class SET hp = 120000, atk_pct_min = 1, atk_pct_max = 2 WHERE tier = 'early' AND class_key IN ('BOSS','MINI','FINAL')");
            st.execute("UPDATE monster_stat_class SET hp = 20000, atk_pct_min = 0.2, atk_pct_max = 0.4 WHERE tier = 'early' AND class_key IN ('MOB','ELITE')");      // the waves of small monsters too
            st.execute("UPDATE static_data_version SET version = version + 1 WHERE domain = 'MONSTER'");
        }
        db = new Database(testDb.url, testDb.user, testDb.password, 8);
        dbExecutor = Executors.newFixedThreadPool(6);
        ServerConfig config = new ServerConfig(0, testDb.url, testDb.user, testDb.password, 8, keystore, "changeit", 100, 5000, 120_000, 15_000, 20, 1 << 20, 4, 30, false, null, 0, "", "", "Test server");
        StaticDataStore staticData = new StaticDataStore(new StaticDataLoader(db));
        PlayerActions actions = new PlayerActions(db, staticData, new EconomyService(db, staticData));
        matches = new MatchManager(db, staticData, actions, dbExecutor, 2, new Random(5), 25.0);
        AuthService auth = new AuthService(new AccountRepository(db), new PasswordHasher(4), config);
        server = new GameServer(config, TlsFactory.serverContext(keystore, "changeit"), auth, dbExecutor, actions, matches);
        port = server.start();
    }

    @AfterAll
    static void stop() throws Exception {
        matches.close();
        server.close();
        dbExecutor.shutdownNow();
        db.close();
        testDb.close();
    }

    // ---- helpers ----
    private record Player(TestClient client, long accountId, String user, long[] characters) {
    }

    /** Registers a user, makes its characters level 30 with some gold and gems, logs in. */
    private Player newPlayer() throws Exception {
        String user = "m" + UUID.randomUUID().toString().replace("-", "").substring(0, 12);
        TestClient c = new TestClient(port);
        c.send(Envelope.newBuilder().setRequestId(1).setHello(Hello.newBuilder().setProtocolVersion(1).setClientVersion("t")).build());
        assertTrue(c.expect(3000).hasHelloAck());
        c.send(Envelope.newBuilder().setRequestId(2).setRegisterRequest(RegisterRequest.newBuilder().setUsername(user).setPassword("password123")).build());
        assertTrue(c.expect(5000).getRegisterResult().getOk());
        long account = scalar("SELECT account_id FROM account WHERE username = ?", user);
        update("UPDATE game_character SET level = 30 WHERE account_id = ?", account);
        update("UPDATE account_wallet SET gold = 100000, gems = 0 WHERE account_id = ?", account);
        c.send(Envelope.newBuilder().setRequestId(3).setLoginRequest(LoginRequest.newBuilder().setUsername(user).setPassword("password123")).build());
        assertTrue(c.expect(5000).getLoginResult().getOk());
        long[] ids = new long[3];
        int i = 0;
        for (String race : new String[]{"EARTH", "NAMEC", "XAYDA"}) {
            ids[i++] = scalar("SELECT character_id FROM game_character WHERE account_id = ? AND race = ?", account, race);
        }
        return new Player(c, account, user, ids);
    }

    private static Envelope startRequest(int rid, int dungeon, long character) {
        return Envelope.newBuilder().setRequestId(rid).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(dungeon).setControlledCharacterId(character)).build();
    }

    /** Plays like a simple client: walk toward the nearest boss or minion and punch. Returns MatchEnded; also reports what it saw. */
    private static final class Played {
        MatchEnded ended;
        int frames;
        int maxEnemies;
        boolean sawBossSpawn;
        long lastTick;
        boolean ticksIncrease = true;
        String tickViolation = "";
    }

    private static Played playUntilEnd(TestClient c, MatchStarted started, long maxMillis) throws Exception {
        Played out = new Played();
        long end = System.currentTimeMillis() + maxMillis;
        int myId = started.getEntitiesList().stream().filter(e -> e.getControlled()).findFirst().orElseThrow().getEntityId();
        java.util.Map<Integer, EntityKind> kinds = new java.util.HashMap<>();
        started.getEntitiesList().forEach(e -> kinds.put(e.getEntityId(), e.getKind()));
        int seq = 0;
        while (System.currentTimeMillis() < end) {
            Envelope e = c.next(2000);
            if (e == null) {
                continue;
            }
            if (e.hasMatchEnded()) {
                out.ended = e.getMatchEnded();
                return out;
            }
            if (!e.hasMatchFrame()) {
                continue;
            }
            MatchFrame f = e.getMatchFrame();
            out.frames++;
            if (f.getServerTick() <= out.lastTick) {
                out.ticksIncrease = false;
                if (out.tickViolation.isEmpty()) {
                    out.tickViolation = "tick " + f.getServerTick() + " after " + out.lastTick + " (frame " + out.frames + ", spawns " + f.getSpawnsCount() + ", effects " + f.getEffectsCount() + ")";
                }
            }
            out.lastTick = f.getServerTick();
            f.getSpawnsList().forEach(s -> {
                kinds.put(s.getEntityId(), s.getKind());
                if (s.getKind() == EntityKind.BOSS) {
                    out.sawBossSpawn = true;
                }
            });
            double mx = 0;
            double my = 0;
            double bx = 0;
            double by = 0;
            double best = Double.MAX_VALUE;
            int enemies = 0;
            int target = 0;
            for (var s : f.getStatesList()) {
                if (s.getEntityId() == myId) {
                    mx = s.getX();
                    my = s.getY();
                }
            }
            for (var s : f.getStatesList()) {
                EntityKind k = kinds.get(s.getEntityId());
                if (s.getAlive() && (k == EntityKind.BOSS || k == EntityKind.MINION)) {
                    enemies++;
                    double dist = Math.hypot(s.getX() - mx, s.getY() - my);
                    if (dist < best) {
                        best = dist;
                        bx = s.getX();
                        by = s.getY();
                        target = s.getEntityId();
                    }
                }
            }
            out.maxEnemies = Math.max(out.maxEnemies, enemies);
            if (target != 0) {
                double dx = bx - mx;
                double dy = by - my;
                double len = Math.max(1, Math.hypot(dx, dy));
                double step = Math.min(len, 11);
                c.send(Envelope.newBuilder().setPlayerStateReport(PlayerStateReport.newBuilder().setSeq(++seq).setX((float) (mx + dx / len * step))
                        .setY((float) (my + dy / len * step)).setDirX((float) dx).setDirY((float) dy)).build());
                c.send(Envelope.newBuilder().setUseSkillRequest(UseSkillRequest.newBuilder().setSeq(++seq).setSlot(0).setTargetId(target)
                        .setDirX((float) dx).setDirY((float) dy)).build());
            }
        }
        throw new AssertionError("the match did not end in time; frames=" + out.frames);
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

    private static String text(String sql, Object... params) throws Exception {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(sql)) {
            for (int i = 0; i < params.length; i++) {
                ps.setObject(i + 1, params[i]);
            }
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getString(1) : null;
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

    /** The match is removed a moment after MatchEnded is sent: wait for it before starting the next one. */
    private static void waitNoMatch(Player p) throws Exception {
        for (int i = 0; i < 100 && matches.hasMatch(p.accountId()); i++) {
            Thread.sleep(50);
        }
        assertFalse(matches.hasMatch(p.accountId()), "the finished match is gone");
    }

    // ---- tests ----
    @Test
    void aWholeMatchOverTheNetworkPaysItsRewardsOnce() throws Exception {
        Player p = newPlayer();
        try (TestClient c = p.client()) {
            c.send(startRequest(10, 12, p.characters()[0]));                        // dungeon 12: Nappa group, early tier, 3 bosses
            Envelope first = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000);
            MatchStarted started = first.getMatchStarted();
            assertEquals(10, first.getRequestId());
            assertTrue(started.getRunId() > 0);
            assertFalse(started.getMatchToken().isEmpty());
            assertEquals(12, started.getDungeonId());
            assertEquals(3, started.getBossCount());
            assertEquals(2, started.getRevivesLeft());
            assertEquals(20, started.getTickRateHz());
            assertEquals(1800f, started.getArena().getWidth());
            assertEquals(3, started.getEntitiesCount(), "the three squad members; bosses come later");
            assertEquals(1, started.getEntitiesList().stream().filter(e -> e.getControlled()).count());
            assertTrue(started.getSkillsList().stream().anyMatch(s -> s.getSlot() == 0), "the basic punch is a skill of the controlled character");

            Played played = playUntilEnd(c, started, 120_000);
            assertEquals(MatchResult.WON, played.ended.getResult());
            assertEquals(3, played.ended.getBossesDefeated());
            assertTrue(played.sawBossSpawn);
            assertTrue(played.ticksIncrease, "server ticks only go up: " + played.tickViolation);
            assertTrue(played.frames > 100);
            assertTrue(played.ended.getGold() > 0);
            assertEquals(20, played.ended.getGems(), "first clear of a dungeon: 20 gems");
            assertTrue(played.ended.getFirstClear());
            assertEquals(3, played.ended.getCharactersCount());
            assertTrue(played.ended.getCharactersList().stream().allMatch(ch -> ch.getExpGained() > 0));

            // the database agrees, and the player data snapshot was pushed again
            var snapshot = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(100_000 + played.ended.getGold(), snapshot.getGold());
            assertEquals(20, snapshot.getGems());
            assertEquals(1, snapshot.getCharacters(0).getFirstClearsCount());
            assertEquals("WON", text("SELECT status FROM match_run WHERE run_id = ?", started.getRunId()));
            assertEquals(1, scalar("SELECT reward_applied FROM match_run WHERE run_id = ?", started.getRunId()));
            assertEquals(3, scalar("SELECT COUNT(*) FROM match_run_character WHERE run_id = ?", started.getRunId()));
            assertEquals(1, scalar("SELECT COUNT(*) FROM character_first_clear WHERE character_id = ?", p.characters()[0]));
            assertEquals(1, scalar("SELECT COUNT(*) FROM currency_ledger WHERE character_id = ? AND reason = 'MATCH_GEMS'", p.characters()[0]));
            assertTrue(scalar("SELECT exp FROM game_character WHERE character_id = ?", p.characters()[0]) > 0);
            assertFalse(matches.hasMatch(p.accountId()), "the finished match is gone");

            // the quests of Ông Gôhan counted the win and the three bosses (docs 15.32), and the snapshot that was pushed already shows it
            assertEquals(1, scalar("SELECT progress FROM quest_progress WHERE account_id = ? AND quest_id = 'd02'", p.accountId()));
            assertEquals(3, scalar("SELECT progress FROM quest_progress WHERE account_id = ? AND quest_id = 'd03'", p.accountId()));
            assertEquals(1, snapshot.getQuestsList().stream().filter(q -> q.getId().equals("d02")).findFirst().orElseThrow().getProgress());

            // a second match of the same dungeon: no first-clear gems again
            c.send(startRequest(11, 12, p.characters()[0]));
            MatchStarted second = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
            assertNotEquals(started.getRunId(), second.getRunId());
            Played again = playUntilEnd(c, second, 120_000);
            assertEquals(MatchResult.WON, again.ended.getResult());
            assertEquals(0, again.ended.getGems());
            assertFalse(again.ended.getFirstClear());
        }
    }

    @Test
    void startingARejectedMatch() throws Exception {
        Player p = newPlayer();
        Player other = newPlayer();
        try (TestClient c = p.client(); TestClient oc = other.client()) {
            c.send(startRequest(1, 9999, p.characters()[0]));
            assertEquals(ActionError.NOT_FOUND, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            c.send(startRequest(2, 12, other.characters()[0]));                              // somebody else's character
            assertEquals(ActionError.NOT_FOUND, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            c.send(startRequest(3, 12, 0));
            assertEquals(ActionError.NOT_FOUND, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            // fusion needs the earring and level 50
            c.send(Envelope.newBuilder().setRequestId(4).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12)
                    .setControlledCharacterId(p.characters()[0]).setFusionPartnerId(p.characters()[1])).build());
            assertEquals(ActionError.NOT_OWNED, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            // a second match while one is running
            c.send(startRequest(5, 12, p.characters()[0]));
            c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000);
            c.send(startRequest(6, 12, p.characters()[0]));
            assertEquals(ActionError.INVALID_ARGUMENT, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            MatchEnded ended = c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000).getMatchEnded();
            assertEquals(MatchResult.ABANDONED, ended.getResult());
            assertEquals(0, ended.getGems());
            assertEquals("ABANDONED", text("SELECT status FROM match_run WHERE run_id = ?", ended.getRunId()));
        }
    }

    @Test
    void aDungeonRunHasADifficultyEverySkillIsReadyAtTheStartAndAHarderRunPaysMore() throws Exception {
        Player p = newPlayer();
        // the village left the punch of the first character cooling down for a minute (kept on the real clock)
        update("INSERT INTO character_skill_cooldown (character_id, skill_id, ready_at_ms) VALUES (?, 0, ?)", p.characters()[0], System.currentTimeMillis() + 60_000);
        try (TestClient c = p.client()) {
            c.send(Envelope.newBuilder().setRequestId(9).setPlayerDataRequest(com.battleball.proto.PlayerDataRequest.getDefaultInstance()).build());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(java.util.List.of("EASY", "HARD", "HELL"), snap.getDifficultiesList().stream().map(x -> x.getKey()).toList(), "the client learns the difficulties from the snapshot");
            assertTrue(snap.getDifficulties(2).getAtkMult() > snap.getDifficulties(0).getAtkMult() && !snap.getDifficulties(1).getName().isEmpty());

            c.send(Envelope.newBuilder().setRequestId(3).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12).setControlledCharacterId(p.characters()[0])
                    .setDifficulty("NIGHTMARE")).build());
            assertEquals(ActionError.NOT_FOUND, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError(), "there is no such difficulty");

            c.send(Envelope.newBuilder().setRequestId(4).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12).setControlledCharacterId(p.characters()[0])
                    .setDifficulty("HELL")).build());
            MatchStarted started = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
            assertEquals("HELL", started.getDifficulty());
            assertEquals("HELL", text("SELECT difficulty FROM match_run WHERE run_id = ?", started.getRunId()));
            assertTrue(started.getSkillsList().stream().anyMatch(sl -> sl.getSkillId() == 0), "the punch is there");
            assertTrue(started.getSkillsList().stream().allMatch(sl -> sl.getCooldownLeftMs() == 0), "every skill is ready when the dungeon begins");
            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000);
            waitNoMatch(p);

            c.send(startRequest(5, 12, p.characters()[0]));                                   // no difficulty named: the easiest
            MatchStarted plain = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
            assertEquals("EASY", plain.getDifficulty());
            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000);
            waitNoMatch(p);

            // a harder run pays more (the same player, the same dungeon, both won)
            MatchEnded[] ended = new MatchEnded[2];
            String[] levels = {"EASY", "HARD"};
            for (int i = 0; i < 2; i++) {
                c.send(Envelope.newBuilder().setRequestId(20 + i).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12).setControlledCharacterId(p.characters()[0])
                        .setDifficulty(levels[i])).build());
                MatchStarted run = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
                ended[i] = playUntilEnd(c, run, 120_000).ended;
                waitNoMatch(p);
                assertEquals(MatchResult.WON, ended[i].getResult(), levels[i]);
            }
            double goldRatio = (double) ended[1].getGold() / ended[0].getGold();
            double expRatio = (double) ended[1].getCharacters(0).getExpGained() / ended[0].getCharacters(0).getExpGained();
            assertTrue(goldRatio > 1.3 && goldRatio < 1.9, "hard pays about 1.6 times the gold of easy: " + goldRatio);
            assertTrue(expRatio > 1.3 && expRatio < 1.9, "...and the EXP: " + expRatio);
        }
    }

    @Test
    void losingTheConnectionPausesTheMatchAndTheTokenBringsItBack() throws Exception {
        Player p = newPlayer();
        MatchStarted started;
        TestClient first = p.client();
        first.send(startRequest(1, 12, p.characters()[0]));
        started = first.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
        MatchFrame before = first.expectCase(Envelope.PayloadCase.MATCH_FRAME, 3000).getMatchFrame();
        first.close();                                                                          // the connection drops
        Thread.sleep(400);
        assertTrue(matches.hasMatch(p.accountId()), "the match waits for the player");

        try (TestClient second = new TestClient(port)) {
            second.send(Envelope.newBuilder().setRequestId(1).setHello(Hello.newBuilder().setProtocolVersion(1).setClientVersion("t")).build());
            assertTrue(second.expect(3000).hasHelloAck());
            // a wrong token does not resume anything
            second.send(Envelope.newBuilder().setRequestId(2).setResumeMatchRequest(ResumeMatchRequest.newBuilder().setMatchToken("nope")).build());
            assertFalse(second.expect(3000).getMatchResumed().getOk());
            second.send(Envelope.newBuilder().setRequestId(3).setResumeMatchRequest(ResumeMatchRequest.newBuilder().setMatchToken(started.getMatchToken())).build());
            var resumed = second.expect(3000).getMatchResumed();
            assertTrue(resumed.getOk());
            assertEquals(started.getRunId(), resumed.getMatch().getRunId());
            assertTrue(resumed.getMatch().getEntitiesCount() >= 3);
            // the client comes back without a login: the player data follows the MatchResumed (owner request 2026-10-11: the dungeon can be entered again after a lost connection)
            var snapshot = second.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            assertEquals(3, snapshot.getCharactersCount());
            assertTrue(snapshot.getDungeonsCount() >= 10);
            MatchFrame after = second.expectCase(Envelope.PayloadCase.MATCH_FRAME, 3000).getMatchFrame();
            assertTrue(after.getServerTick() >= before.getServerTick(), "the simulation continues where it stopped");
            // the resumed connection is logged in as the same account: it can leave the match
            second.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            MatchEnded ended = second.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000).getMatchEnded();
            assertEquals(MatchResult.ABANDONED, ended.getResult());
        }
        Thread.sleep(200);
        assertFalse(matches.hasMatch(p.accountId()));
    }

    @Test
    void aFusionMatchHasTheMainCharacterAddedToItsPartnerAndOneAlly() throws Exception {
        Player p = newPlayer();
        update("UPDATE game_character SET level = 50 WHERE account_id = ?", p.accountId());
        update("UPDATE account SET potara_owned = 1 WHERE account_id = ?", p.accountId());
        try (TestClient c = p.client()) {
            // the snapshot pushed after the login may have been built before the level was raised above (the player is loaded on another thread): ask again and take the
            // first snapshot that shows level 50 (this was the cause of a rare failure: 372739, the level-30 figure, instead of 430528)
            c.send(Envelope.newBuilder().setRequestId(9).setPlayerDataRequest(com.battleball.proto.PlayerDataRequest.getDefaultInstance()).build());
            var snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            while (snap.getCharacters(0).getLevel() != 50) {
                snap = c.expectCase(Envelope.PayloadCase.PLAYER_DATA_SNAPSHOT, 5000).getPlayerDataSnapshot();
            }
            long mainHp = snap.getCharacters(0).getStats().getHpMax();
            long partnerHp = snap.getCharacters(1).getStats().getHpMax();
            assertEquals(430_528, mainHp);
            // same level: fusing the same character with itself, or a partner that does not exist, is refused
            c.send(Envelope.newBuilder().setRequestId(1).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12)
                    .setControlledCharacterId(p.characters()[0]).setFusionPartnerId(p.characters()[0])).build());
            assertEquals(ActionError.NOT_FOUND, c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 5000).getActionResult().getError());
            c.send(Envelope.newBuilder().setRequestId(2).setStartMatchRequest(StartMatchRequest.newBuilder().setDungeonId(12)
                    .setControlledCharacterId(p.characters()[0]).setFusionPartnerId(p.characters()[1])).build());
            MatchStarted started = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
            assertEquals(2, started.getEntitiesCount(), "one fused fighter and one AI ally");
            var fused = started.getEntitiesList().stream().filter(e -> e.getControlled()).findFirst().orElseThrow();
            assertEquals(mainHp + partnerHp, fused.getHpMax(), "fusion adds the stats of both characters");
            assertTrue(fused.getName().contains("hợp thể"));
            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            assertEquals(MatchResult.ABANDONED, c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000).getMatchEnded().getResult());
            // the partner gets its own EXP row only when a result with EXP exists; an abandoned match with no kills pays nothing
            assertEquals(0, scalar("SELECT gold_earned FROM match_run WHERE run_id = ?", started.getRunId()));
        }
    }

    @Test
    void thePlayerCannotMoveOrCastWithoutAMatch() throws Exception {
        Player p = newPlayer();
        try (TestClient c = p.client()) {
            c.send(Envelope.newBuilder().setPlayerStateReport(PlayerStateReport.newBuilder().setX(1).setY(2)).build());
            c.send(Envelope.newBuilder().setRequestId(7).setPing(com.battleball.proto.Ping.getDefaultInstance()).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.PONG, 3000).hasPong(), "stray match messages are ignored and the connection stays healthy");
            assertFalse(matches.hasMatch(p.accountId()));
        }
    }

    @Test
    void gearWornDuringATrainingSessionChangesTheFightersAtOnce() throws Exception {
        Player p = newPlayer();
        update("INSERT INTO item_instance (account_id, template_id, slot, upgrade_percent) VALUES (?, 1, 'ARMOR', 10)", p.accountId());       // Áo Thần Linh Trái Đất, +10%: Giáp 2.750
        long armorItem = scalar("SELECT item_id FROM item_instance WHERE account_id = ? AND template_id = 1", p.accountId());
        try (TestClient c = p.client()) {
            c.send(Envelope.newBuilder().setRequestId(30).setStartTrainingRequest(StartTrainingRequest.newBuilder().setControlledCharacterId(p.characters()[0])
                    .setArenaW(3000).setArenaH(800).addSpots(TrainingSpot.newBuilder().setKey("moc_nhan").setX(900).setY(200))).build());
            MatchStarted s = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted();
            int me = s.getEntitiesList().stream().filter(e -> e.getControlled()).findFirst().orElseThrow().getEntityId();
            long before = armorOf(c, me, -1);
            c.send(Envelope.newBuilder().setRequestId(31).setEquipItemRequest(com.battleball.proto.EquipItemRequest.newBuilder()
                    .setCharacterId(p.characters()[0]).setItemId(armorItem)).build());
            long worn = armorOf(c, me, before + 2750);
            assertEquals(before + 2750, worn, "the fighter of the running session takes the armor of the piece at once (+2.500 and 10%)");
            c.send(Envelope.newBuilder().setRequestId(32).setUnequipItemRequest(com.battleball.proto.UnequipItemRequest.newBuilder()
                    .setCharacterId(p.characters()[0]).setSlot("ARMOR")).build());
            assertEquals(before, armorOf(c, me, before), "and loses it again when the piece comes off");
        }
    }

    /** The armor of this entity in the frames: the first one when {@code want} is negative, else the first one that equals it (or the last seen after 8 s). */
    private static long armorOf(TestClient c, int entity, long want) throws Exception {
        long last = -1;
        long end = System.currentTimeMillis() + 8000;
        while (System.currentTimeMillis() < end) {
            Envelope e = c.next(1000);
            if (e == null || !e.hasMatchFrame()) {
                continue;
            }
            for (var st : e.getMatchFrame().getStatesList()) {
                if (st.getEntityId() == entity && st.getArmor() > 0) {
                    last = st.getArmor();
                    if (want < 0 || last == want) {
                        return last;
                    }
                }
            }
        }
        return last;
    }

    @Test
    void aTrainingSessionOffersEverySkillOfTheRaceAndRecordsNothing() throws Exception {
        Player p = newPlayer();
        update("INSERT INTO character_skill (character_id, skill_id, `level`) VALUES (?, 1, 1)", p.characters()[0]);       // Kamejoko learned too
        try (TestClient c = p.client()) {
            c.send(Envelope.newBuilder().setRequestId(30).setStartTrainingRequest(StartTrainingRequest.newBuilder().setControlledCharacterId(p.characters()[0])
                    .setArenaW(3000).setArenaH(800).addSpots(TrainingSpot.newBuilder().setKey("moc_nhan").setX(900).setY(200))
                    .addSpots(TrainingSpot.newBuilder().setKey("saibamen").setX(1500).setY(200))).build());
            Envelope first = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000);
            assertEquals(30, first.getRequestId());
            MatchStarted s = first.getMatchStarted();
            assertTrue(s.getTraining());
            assertEquals(0, s.getRunId());
            assertEquals(3000f, s.getArena().getWidth());
            var dummies = s.getEntitiesList().stream().filter(e -> e.getKind() == EntityKind.TRAINING_DUMMY).toList();
            assertEquals(2, dummies.size());
            var moc = dummies.stream().filter(e -> e.getDummyKey().equals("moc_nhan")).findFirst().orElseThrow();
            var saiba = dummies.stream().filter(e -> e.getDummyKey().equals("saibamen")).findFirst().orElseThrow();
            assertEquals("Mộc nhân", moc.getName());
            assertEquals(2_000_000_000L, moc.getHpMax());
            assertEquals(0, moc.getMobTemplateId());
            assertEquals(647, saiba.getCostumeId());
            assertEquals(-1, saiba.getMobTemplateId());
            // owner request 2026-10-09: skills not learned are not usable in training either; a new character knows only the punch
            assertEquals(2, s.getSkillsCount(), "only the punch and Kamejoko are learned");
            assertTrue(s.getSkillsList().stream().allMatch(sl -> sl.getSkillId() == 0 || sl.getSkillId() == 1));

            int seq = 0;
            boolean hit = false;
            long end = System.currentTimeMillis() + 10_000;
            while (!hit && System.currentTimeMillis() < end) {
                Envelope e = c.next(1000);
                if (e == null || !e.hasMatchFrame()) {
                    continue;
                }
                c.send(Envelope.newBuilder().setPlayerStateReport(PlayerStateReport.newBuilder().setSeq(++seq).setX(moc.getX() - 30).setY(moc.getY()).setDirX(1)).build());
                c.send(Envelope.newBuilder().setUseSkillRequest(UseSkillRequest.newBuilder().setSeq(++seq).setSlot(0).setTargetId(moc.getEntityId()).setDirX(1)).build());
                c.send(Envelope.newBuilder().setUseSkillRequest(UseSkillRequest.newBuilder().setSeq(++seq).setSlot(1).setTargetId(moc.getEntityId()).setDirX(1)).build());
                hit = e.getMatchFrame().getDamagesList().stream().anyMatch(d -> d.getTargetId() == moc.getEntityId() && d.getAmount() > 0);
            }
            assertTrue(hit, "the punch hits the Mộc nhân");

            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            MatchEnded ended = c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000).getMatchEnded();
            assertEquals(MatchResult.ABANDONED, ended.getResult());
            assertEquals(0, scalar("SELECT COUNT(*) FROM match_run WHERE account_id = ?", p.accountId()), "training is not recorded");
            // the skills used are still cooling down: their ready time (real clock) is kept so that logging out does not reset them
            long kept = 0;
            for (int i = 0; i < 30 && kept == 0; i++) {
                kept = scalar("SELECT COUNT(*) FROM character_skill_cooldown WHERE character_id = ? AND ready_at_ms > ?", p.characters()[0], System.currentTimeMillis() - 60_000);
                if (kept == 0) {
                    Thread.sleep(100);
                }
            }
            assertTrue(kept > 0, "the cooldown of a skill used in training is kept in the database");
        }
    }

    @Test
    void aRealMatchReplacesTheTrainingSessionOfTheVillage() throws Exception {
        Player p = newPlayer();
        try (TestClient c = p.client()) {
            c.send(Envelope.newBuilder().setRequestId(40).setStartTrainingRequest(StartTrainingRequest.newBuilder().setControlledCharacterId(p.characters()[0])
                    .setArenaW(3000).setArenaH(800).addSpots(TrainingSpot.newBuilder().setKey("moc_nhan").setX(900).setY(200))).build());
            assertTrue(c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000).getMatchStarted().getTraining());
            // Cui sends the player to a dungeon while the village session still runs: the session closes quietly and the dungeon starts
            c.send(startRequest(41, 12, p.characters()[0]));
            Envelope answer = c.expectCase(Envelope.PayloadCase.MATCH_STARTED, 8000);
            assertEquals(41, answer.getRequestId());
            assertFalse(answer.getMatchStarted().getTraining());
            assertTrue(answer.getMatchStarted().getRunId() > 0);
            // a second real match is still refused
            c.send(startRequest(42, 12, p.characters()[0]));
            assertFalse(c.expectCase(Envelope.PayloadCase.ACTION_RESULT, 8000).getActionResult().getOk());
            c.send(Envelope.newBuilder().setLeaveMatchRequest(LeaveMatchRequest.getDefaultInstance()).build());
            c.expectCase(Envelope.PayloadCase.MATCH_ENDED, 8000);
        }
    }
}
