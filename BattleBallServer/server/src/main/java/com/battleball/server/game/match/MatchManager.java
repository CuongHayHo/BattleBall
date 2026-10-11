package com.battleball.server.game.match;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.Envelope;
import com.battleball.proto.MatchEnded;
import com.battleball.proto.MatchResult;
import com.battleball.proto.MatchResumed;
import com.battleball.proto.StartMatchRequest;
import com.battleball.proto.StartTrainingRequest;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.PlayerActions;
import com.battleball.server.game.PlayerRepository;
import com.battleball.server.game.StatCalculator;
import io.netty.channel.Channel;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.security.SecureRandom;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.ScheduledThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import java.util.random.RandomGenerator;

/**
 * Runs the matches: a fixed pool of threads, each match a task repeating every 50 ms (one thread can serve several matches; no thread per player),
 * as decided in docs/networking/BattleBall.md. Pays finished matches on the DB executor so that a tick never waits for the database.
 * Connection loss: the match pauses up to 30 seconds, twice per match, then it is abandoned (CONFIRMED, docs/protocol/BattleBall.md section 2).
 * Training sessions of the village run here too but are never paid, recorded or resumed: they end at once when the connection goes.
 */
public final class MatchManager implements AutoCloseable {
    private static final Logger LOG = LoggerFactory.getLogger(MatchManager.class);

    private final Database db;
    private final MatchStarter starter;
    private final MatchSettlement settlement;
    private final PlayerActions playerActions;
    private final StaticDataStore data;
    private final ExecutorService dbExecutor;
    private final ScheduledExecutorService pool;
    private final RandomGenerator random;
    private final long periodMicros;
    private final ConcurrentHashMap<Long, Running> byAccount = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, Running> byToken = new ConcurrentHashMap<>();

    private static final class Running {
        final MatchStarter.Started started;
        final MatchEngine engine;
        volatile Channel channel;
        volatile ScheduledFuture<?> task;
        volatile long pausedAtMs;           // wall clock when the pause began, 0 = not paused
        int pausesUsed;
        volatile boolean settling;

        Running(MatchStarter.Started started, Channel channel) {
            this.started = started;
            this.engine = started.engine();
            this.channel = channel;
        }

        boolean training() {
            return started.setup().training() != null;
        }
    }

    public MatchManager(Database db, StaticDataStore data, PlayerActions playerActions, ExecutorService dbExecutor, int threads, RandomGenerator random) {
        this(db, data, playerActions, dbExecutor, threads, random, 1.0);
    }

    /** {@code speed} above 1 runs the simulation faster than real time (tests only); the game itself always uses 1. */
    public MatchManager(Database db, StaticDataStore data, PlayerActions playerActions, ExecutorService dbExecutor, int threads, RandomGenerator random, double speed) {
        this.periodMicros = Math.max(200, Math.round(MatchEngine.TICK_MS * 1000.0 / speed));
        this.db = db;
        this.starter = new MatchStarter(db, data);
        this.settlement = new MatchSettlement(db);
        this.playerActions = playerActions;
        if (playerActions != null) {
            playerActions.onStatsChanged(this::refreshStats);
        }
        this.data = data;
        this.dbExecutor = dbExecutor;
        this.random = random;
        ScheduledThreadPoolExecutor p = new ScheduledThreadPoolExecutor(Math.max(1, threads), r -> {
            Thread t = new Thread(r, "match-tick");
            t.setDaemon(true);
            return t;
        });
        p.setRemoveOnCancelPolicy(true);
        this.pool = p;
    }

    /** Marks runs left ACTIVE by a server that stopped as EXPIRED (nothing is paid for them). Call once at startup. */
    public static void expireStaleRuns(Database db) throws SQLException {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement("UPDATE match_run SET status = 'EXPIRED', ended_at = NOW() WHERE status = 'ACTIVE'")) {
            int n = ps.executeUpdate();
            if (n > 0) {
                LOG.info("{} unfinished match(es) from an earlier run marked EXPIRED", n);
            }
        }
    }

    public boolean hasMatch(long accountId) {
        return byAccount.containsKey(accountId);
    }

    public int running() {
        return byAccount.size();
    }

    // ---------------------------------------------------------------------------------------------------------------- start / resume

    /** Blocking (database): call it from the DB executor. Replies on the channel. */
    public void start(long accountId, int requestId, StartMatchRequest req, Channel channel) {
        try {
            Running old = byAccount.get(accountId);
            if (old != null && !old.training()) {
                reply(channel, requestId, ActionError.INVALID_ARGUMENT, "a match is already running: resume it");
                return;
            }
            if (old != null) {                            // the training session of the village closes quietly when a real match starts (owner request 2026-10-11: Cui sends the player to a dungeon)
                old.channel = null;
                old.engine.submit(old.engine::leave);
                stop(old);
            }
            MatchStarter.Started s = starter.start(accountId, req, random);
            Running r = new Running(s, channel);
            if (byAccount.putIfAbsent(accountId, r) != null) {
                reply(channel, requestId, ActionError.INVALID_ARGUMENT, "a match is already running");
                return;
            }
            byToken.put(MatchStarter.sha256(s.token()), r);
            channel.writeAndFlush(Envelope.newBuilder().setRequestId(requestId).setMatchStarted(MatchStarter.toProto(s, r.engine, true)).build());
            r.task = pool.scheduleAtFixedRate(() -> tick(r), periodMicros, periodMicros, TimeUnit.MICROSECONDS);
        } catch (MatchStarter.StartException e) {
            reply(channel, requestId, e.error, e.getMessage());
        } catch (SQLException | RuntimeException e) {
            LOG.error("cannot start a match", e);
            reply(channel, requestId, ActionError.ACTION_INTERNAL, "server error");
        }
    }

    private static void reply(Channel ch, int rid, ActionError e, String message) {
        ch.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setActionResult(ActionResult.newBuilder().setOk(false).setError(e).setMessage(message)).build());
    }

    /**
     * Starts a training session of the village (blocking: reads the player; call it from the DB executor). A training session already running for the account is
     * replaced; a real match is not.
     */
    public void startTraining(long accountId, int requestId, StartTrainingRequest req, Channel channel) {
        try {
            Running old = byAccount.get(accountId);
            if (old != null && !old.training()) {
                reply(channel, requestId, ActionError.INVALID_ARGUMENT, "a match is running: finish it first");
                return;
            }
            if (old != null) {
                old.channel = null;                       // the old session closes quietly
                old.engine.submit(old.engine::leave);
                stop(old);
            }
            MatchStarter.Started s = starter.startTraining(accountId, req, random);
            Running r = new Running(s, channel);
            if (byAccount.putIfAbsent(accountId, r) != null) {
                reply(channel, requestId, ActionError.INVALID_ARGUMENT, "a match is already running");
                return;
            }
            r.engine.onFirstDummyHit(() -> {                      // the quest "tập đánh Mộc nhân" (docs 15.32); the engine thread only hands the work over
                Runnable count = () -> {
                    try {
                        if (playerActions.recordQuest(accountId, "TRAINING", 1)) {
                            Channel ch = r.channel;
                            if (ch != null && ch.isActive()) {
                                ch.writeAndFlush(playerActions.snapshotEnvelope(accountId, 0));
                            }
                        }
                    } catch (SQLException | RuntimeException e) {
                        LOG.warn("cannot tell the client about the training quest", e);
                    }
                };
                try {
                    dbExecutor.execute(count);
                } catch (RuntimeException e) {
                    LOG.warn("cannot count the training quest", e);
                }
            });
            channel.writeAndFlush(Envelope.newBuilder().setRequestId(requestId).setMatchStarted(MatchStarter.toProto(s, r.engine, true)).build());
            r.task = pool.scheduleAtFixedRate(() -> tick(r), periodMicros, periodMicros, TimeUnit.MICROSECONDS);
        } catch (MatchStarter.StartException e) {
            reply(channel, requestId, e.error, e.getMessage());
        } catch (SQLException | RuntimeException e) {
            LOG.error("cannot start a training session", e);
            reply(channel, requestId, ActionError.ACTION_INTERNAL, "server error");
        }
    }

    /**
     * Gear, a skin, an aura or a support changed in the village while a training session runs: the fighters take the new figures at once, without a restart
     * (owner request 2026-10-10). Blocking (reads the player): call it from the DB executor. A real match is left alone.
     */
    public void refreshStats(long accountId) {
        Running r = byAccount.get(accountId);
        if (r == null || !r.training() || r.settling) {
            return;
        }
        try (Connection c = db.connection()) {
            StaticData d = data.get();
            Map<Long, StatCalculator.CharacterStats> figures = MatchStarter.currentFigures(d, PlayerRepository.load(c, accountId), r.started.setup());
            r.engine.submit(() -> r.engine.applyStats(figures));
        } catch (SQLException | RuntimeException e) {
            LOG.warn("cannot refresh the figures of a training session", e);
        }
    }

    /** Ends a training session at once: nothing to pay or record. */
    private void stop(Running r) {
        saveCooldowns(r, false);
        r.settling = true;
        ScheduledFuture<?> t = r.task;
        if (t != null) {
            t.cancel(false);
        }
        byAccount.remove(r.started.accountId(), r);
    }

    /** A new connection takes over a paused (or orphaned) match. Returns the account id, or 0 when the token is not valid. */
    public long resume(int requestId, String token, Channel channel) {
        Running r = byToken.get(MatchStarter.sha256(token == null ? "" : token));
        if (r == null || r.settling) {
            channel.writeAndFlush(Envelope.newBuilder().setRequestId(requestId).setMatchResumed(MatchResumed.newBuilder().setOk(false).setError("no such match")).build());
            return 0;
        }
        Channel old = r.channel;
        r.channel = channel;
        r.pausedAtMs = 0;
        r.engine.submit(() -> r.engine.setPaused(false));
        channel.writeAndFlush(Envelope.newBuilder().setRequestId(requestId).setMatchResumed(MatchResumed.newBuilder().setOk(true)
                .setMatch(MatchStarter.toProto(r.started, r.engine, false))).build());
        LOG.info("match {} resumed", r.started.runId());
        return r.started.accountId();
    }

    // ---------------------------------------------------------------------------------------------------------------- inputs from the client

    public void report(long accountId, com.battleball.proto.PlayerStateReport m) {
        Running r = byAccount.get(accountId);
        if (r != null) {
            r.engine.submit(() -> {
                r.engine.reportPosition(m.getSeq(), m.getX(), m.getY(), m.getDirX(), m.getDirY());
                if (m.getViewW() > 0 && m.getViewH() > 0) {
                    r.engine.reportView(m.getViewX(), m.getViewY(), m.getViewW(), m.getViewH());
                }
                for (com.battleball.proto.AllyPosition a : m.getAlliesList()) {
                    r.engine.reportAlly(a.getEntityId(), a.getX(), a.getY(), a.getDirX(), a.getDirY());
                }
            });
        }
    }

    public void useSkill(long accountId, com.battleball.proto.UseSkillRequest m) {
        Running r = byAccount.get(accountId);
        if (r != null) {
            r.engine.submit(() -> r.engine.useSkill(m.getSeq(), m.getSlot(), m.getTargetId(), m.getDirX(), m.getDirY()));
        }
    }

    /** The player takes over another fighter of the squad: the answer carries the new fighter's skills (docs 15.26). */
    public void switchControl(long accountId, int entityId) {
        Running r = byAccount.get(accountId);
        if (r != null) {
            r.engine.submit(() -> {
                var f = r.engine.switchControl(entityId);
                var reply = com.battleball.proto.ControlSwitched.newBuilder().setOk(f != null);
                if (f != null) {
                    reply.setEntityId(f.id).addAllSkills(MatchStarter.skillSlots(r.engine, f));
                }
                Channel ch = r.channel;
                if (ch != null) {
                    ch.writeAndFlush(Envelope.newBuilder().setControlSwitched(reply).build());
                }
            });
        }
    }

    public void revive(long accountId) {
        Running r = byAccount.get(accountId);
        if (r != null) {
            r.engine.submit(r.engine::revive);
        }
    }

    public void leave(long accountId) {
        Running r = byAccount.get(accountId);
        if (r != null) {
            r.engine.submit(r.engine::leave);
        }
    }

    /** The connection of this account closed. */
    public void onDisconnect(long accountId, Channel channel) {
        Running r = byAccount.get(accountId);
        if (r == null || r.channel != channel || r.settling) {
            return;
        }
        if (r.training()) {
            r.engine.submit(r.engine::leave);           // training cannot be resumed: it simply ends
            return;
        }
        StaticData d = data.get();
        int allowed = (int) Json.num(d.balanceObj("match_rules"), "pauses_per_match", 2);
        r.pausesUsed++;
        if (r.pausesUsed > allowed) {
            r.engine.submit(r.engine::leave);       // out of pauses: the match is abandoned
            return;
        }
        r.pausedAtMs = System.currentTimeMillis();
        r.engine.submit(() -> r.engine.setPaused(true));
        LOG.info("match {} paused (connection lost, pause {} of {})", r.started.runId(), r.pausesUsed, allowed);
    }

    // ---------------------------------------------------------------------------------------------------------------- the tick

    private void tick(Running r) {
        if (r.settling) {
            return;                 // the match is over and settled (or being settled): a tick that was already queued must not send one more frame with the same tick number
        }
        try {
            if (r.pausedAtMs != 0) {
                long maxMs = (long) (Json.num(r.started.data().balanceObj("match_rules"), "pause_max_s", 30) * 1000);
                if (System.currentTimeMillis() - r.pausedAtMs > maxMs) {
                    r.pausedAtMs = 0;
                    r.engine.submit(r.engine::leave);       // too long: abandoned
                    r.engine.setPaused(false);
                }
            }
            r.engine.step();
            Channel ch = r.channel;
            if (ch != null && ch.isActive() && !r.engine.paused()) {
                ch.writeAndFlush(Envelope.newBuilder().setMatchFrame(r.engine.buildFrame()).build());
            } else {
                r.engine.buildFrame();     // keep the event buffers from growing while nobody listens
            }
            if (r.engine.finished()) {
                finish(r);
            }
        } catch (RuntimeException e) {
            LOG.error("match {} crashed", r.started.runId(), e);
            r.engine.leave();
            finish(r);
        }
    }

    private void finish(Running r) {
        if (r.settling) {
            return;
        }
        r.settling = true;
        ScheduledFuture<?> t = r.task;
        if (t != null) {
            t.cancel(false);
        }
        MatchOutcome out = r.engine.outcome();
        saveCooldowns(r, true);
        if (r.training()) {
            // training: no reward and no record, the client only learns that the session is over
            Channel ch = r.channel;
            if (ch != null && ch.isActive()) {
                ch.writeAndFlush(Envelope.newBuilder().setMatchEnded(MatchEnded.newBuilder().setResult(out.result()).setDurationS(out.durationS())).build());
            }
            byAccount.remove(r.started.accountId(), r);
            return;
        }
        Runnable work = () -> {
            try {
                MatchEnded ended = settlement.settle(r.started, out, random);
                if (out.result() == com.battleball.proto.MatchResult.WON) {         // the quests of Ông Gôhan (docs 15.32)
                    playerActions.recordQuest(r.started.accountId(), "DUNGEON_WIN", 1);
                }
                if (out.bossesDefeated() > 0) {
                    playerActions.recordQuest(r.started.accountId(), "BOSS_KILL", out.bossesDefeated());
                }
                Channel ch = r.channel;
                if (ch != null && ch.isActive()) {
                    ch.writeAndFlush(Envelope.newBuilder().setMatchEnded(ended).build());
                    ch.writeAndFlush(playerActions.snapshotEnvelope(r.started.accountId(), 0));
                }
                LOG.info("match {} ended: {}, bosses {}/{}, {} s", r.started.runId(), out.result(), out.bossesDefeated(), out.bossCount(), out.durationS());
            } catch (SQLException | RuntimeException e) {
                LOG.error("cannot settle match {}", r.started.runId(), e);
            } finally {
                byAccount.remove(r.started.accountId(), r);
                byToken.values().remove(r);
            }
        };
        try {
            dbExecutor.execute(work);
        } catch (RuntimeException e) {
            work.run();
        }
    }

    /** Keeps the cooldowns that are still running (real clock): the next session, even after a log out, starts with what is left. */
    private void saveCooldowns(Running r, boolean async) {
        long wall = System.currentTimeMillis();
        java.util.Map<Long, java.util.Map<Integer, Long>> ready = new java.util.HashMap<>();
        try {
            r.engine.cooldownsLeft().forEach((ch, skills) -> {
                java.util.Map<Integer, Long> m = new java.util.HashMap<>();
                skills.forEach((id, left) -> m.put(id, wall + left));
                ready.put(ch, m);
            });
        } catch (RuntimeException e) {
            LOG.warn("cannot read the cooldowns of match {}", r.started.runId(), e);
            return;
        }
        if (ready.isEmpty()) {
            return;
        }
        Runnable work = () -> {
            try (Connection c = db.connection()) {
                com.battleball.server.game.PlayerRepository.saveCooldowns(c, ready);
            } catch (SQLException | RuntimeException e) {
                LOG.warn("cannot keep the skill cooldowns", e);
            }
        };
        if (!async) {
            work.run();
            return;
        }
        try {
            dbExecutor.execute(work);
        } catch (RuntimeException e) {
            work.run();
        }
    }

    @Override
    public void close() {
        for (Running r : List.copyOf(byAccount.values())) {
            r.engine.submit(r.engine::leave);
        }
        pool.shutdown();
        try {
            pool.awaitTermination(2, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    public static MatchResult abandoned() {
        return MatchResult.ABANDONED;
    }

    public static RandomGenerator defaultRandom() {
        return new SecureRandom();
    }
}
