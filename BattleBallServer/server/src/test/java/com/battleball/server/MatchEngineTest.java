package com.battleball.server;

import com.battleball.proto.DamageEvent;
import com.battleball.proto.Effect;
import com.battleball.proto.MatchFrame;
import com.battleball.proto.MatchResult;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.db.Database;
import com.battleball.server.game.StatCalculator;
import com.battleball.server.game.match.Entities;
import com.battleball.server.game.match.MatchEngine;
import com.battleball.server.game.match.MatchOutcome;
import com.battleball.server.game.match.MatchSetup;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Random;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The match simulation against the real static data (loaded from a throw-away database). No network. */
@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class MatchEngineTest {
    private static TestDb testDb;
    private static StaticData d;

    @BeforeAll
    static void load() throws Exception {
        testDb = TestDb.create();
        try (Database db = new Database(testDb.url, testDb.user, testDb.password, 2)) {
            d = new StaticDataLoader(db).load();
        }
    }

    @AfterAll
    static void drop() throws Exception {
        testDb.close();
    }

    // ---- helpers ----
    private static MatchSetup.SquadMember member(long id, String race, int level, boolean controlled, Map<Integer, Integer> skills) {
        StatCalculator.CharacterStats s = StatCalculator.compute(d, level, List.of(), List.of());
        return new MatchSetup.SquadMember(id, race, race, level, controlled, false, 0, s, skills, 0, null);
    }

    private static MatchSetup.SquadMember member(long id, String race, int level, boolean controlled, Map<Integer, Integer> skills, double atkMult, double hpMult) {
        StatCalculator.CharacterStats s = StatCalculator.compute(d, level, List.of(), List.of());
        s = new StatCalculator.CharacterStats(Math.round(s.hpMax() * hpMult), s.kiMax(), Math.round(s.atk() * atkMult), s.armor(), s.critRatePct(), s.critDmgPct(),
                s.dodgePct(), s.hpRegenPctPerSec(), s.kiRegenPctPerSec(), s.other());
        return new MatchSetup.SquadMember(id, race, race, level, controlled, false, 0, s, skills, 0, null);
    }

    private static List<MatchSetup.Encounter> encounters(int dungeonId, int max) {
        StaticData.Dungeon dg = d.dungeons().get(dungeonId);
        List<MatchSetup.Encounter> out = new ArrayList<>();
        MatchSetup.Encounter fin = null;
        for (StaticData.DungeonBoss b : dg.bosses()) {
            if (b.role().equals("MINION")) {
                continue;
            }
            MatchSetup.Encounter e = new MatchSetup.Encounter(d.bosses().get(b.bossId()), b.role(), b.mobTier());
            if (b.role().equals("FINAL")) {
                fin = e;
            } else if (out.size() < max - 1) {
                out.add(e);
            }
        }
        if (fin != null) {
            out.add(fin);
        }
        return out;
    }

    private static List<MatchSetup.Encounter> minions(int dungeonId) {
        List<MatchSetup.Encounter> out = new ArrayList<>();
        for (StaticData.DungeonBoss b : d.dungeons().get(dungeonId).bosses()) {
            if (b.role().equals("MINION")) {
                out.add(new MatchSetup.Encounter(d.bosses().get(b.bossId()), "MINION", b.mobTier()));
            }
        }
        return out;
    }

    private static MatchEngine engine(int dungeonId, int bosses, List<MatchSetup.SquadMember> squad, long seed) {
        MatchSetup s = new MatchSetup(1, d, d.dungeons().get(dungeonId), squad, encounters(dungeonId, bosses), minions(dungeonId), 10_000_000, new Random(seed));
        return new MatchEngine(s);
    }

    private static final Map<Integer, Integer> EARTH_SKILLS = Map.of(0, 7, 1, 7, 9, 7);          // punch, Kamejoko, Kaioken at level 7
    private static final Map<Integer, Integer> NAMEC_SKILLS = Map.of(2, 7, 3, 7, 7, 7, 19, 5);
    private static final Map<Integer, Integer> XAYDA_SKILLS = Map.of(4, 7, 5, 7, 8, 7, 13, 7);

    /** A scripted player: walk into punching range of the nearest enemy and punch (slot 0) whenever possible; Kamejoko (slot 1) from range. */
    private static void play(MatchEngine e) {
        Entities.Fighter me = e.controlledFighter();
        if (me == null || !me.alive) {
            return;
        }
        Entities.Entity best = null;
        for (Entities.Entity x : e.entities()) {
            if (x.alive && x.team == Entities.Team.ENEMY && (best == null || me.distance(x) < me.distance(best))) {
                best = x;
            }
        }
        if (best == null) {
            return;
        }
        double dx = best.x - me.x;
        double dy = best.y - me.y;
        double dist = Math.hypot(dx, dy);
        final Entities.Entity target = best;
        if (dist > best.radius + 30) {
            double step = 220 * MatchEngine.TICK_MS / 1000.0;
            e.submit(() -> e.reportPosition(0, me.x + dx / dist * step, me.y + dy / dist * step, dx, dy));
        }
        e.submit(() -> {
            if (!e.useSkill(0, 0, target.id, dx, dy)) {
                e.useSkill(0, 1, target.id, dx, dy);
            }
        });
    }

    private static MatchOutcome run(MatchEngine e, int maxSeconds) {
        for (int i = 0; i < maxSeconds * 20 && !e.finished(); i++) {
            play(e);
            e.step();
            e.buildFrame();
        }
        return e.outcome();
    }

    // ---- tests ----
    @Test
    void aStrongSquadBeatsADungeonAndEarnsRewards() {
        // dungeon 12 (Nappa group, early tier): 3 bosses; level-100 characters with a big attack
        MatchEngine e = engine(12, 3, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 3), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 3),
                member(3, "XAYDA", 100, false, XAYDA_SKILLS, 8, 3)), 7);
        MatchOutcome out = run(e, 900);
        assertEquals(MatchResult.WON, out.result());
        assertEquals(3, out.bossesDefeated());
        assertTrue(out.finalBossDefeated());
        assertTrue(out.goldEarned() > 0);
        assertEquals(3, out.expRaw().size());
        assertTrue(out.expRaw().values().stream().allMatch(v -> v > 0));
        assertEquals(3, out.kills().stream().filter(MatchOutcome.Kill::realBoss).count());
        assertEquals("FINAL", out.kills().get(out.kills().size() - 1).classKey(), "the final boss dies last");
    }

    @Test
    void aWeakSquadLoses() {
        MatchEngine e = engine(7, 2, List.of(member(1, "EARTH", 1, true, Map.of(0, 1), 1, 1)), 3);   // dungeon 7 is a late-tier dungeon (Cumber)
        MatchOutcome out = run(e, 600);
        assertEquals(MatchResult.LOST, out.result());
        assertEquals(0, out.bossesDefeated());
    }

    @Test
    void bossUsesItsActionsAndTelegraphsTheUltimate() {
        MatchEngine e = engine(13, 2, List.of(member(1, "EARTH", 100, true, Map.of(0, 7), 0.0001, 40), member(2, "NAMEC", 100, false, Map.of(2, 7), 0.0001, 40)), 11);
        boolean sawTelegraph = false;
        boolean sawStun = false;
        int damageEvents = 0;
        for (int i = 0; i < 20 * 120 && !e.finished(); i++) {
            e.step();
            MatchFrame f = e.buildFrame();
            sawTelegraph |= f.getEffectsList().stream().anyMatch(x -> x.getEffect() == Effect.TELEGRAPH_RECT && x.getDurationMs() == 2000);
            sawStun |= f.getEffectsList().stream().anyMatch(x -> x.getEffect() == Effect.STUN);
            damageEvents += f.getDamagesCount();
        }
        assertTrue(sawTelegraph, "the boss ultimate must be announced 2 seconds before it lands");
        assertTrue(sawStun, "the boss area stun must happen");
        assertTrue(damageEvents > 20);
    }

    @Test
    void pausedMatchDoesNotAdvance() {
        MatchEngine e = engine(12, 2, List.of(member(1, "EARTH", 50, true, EARTH_SKILLS)), 1);
        for (int i = 0; i < 20; i++) {
            e.step();
        }
        long t = e.nowMs();
        e.setPaused(true);
        for (int i = 0; i < 40; i++) {
            e.step();
        }
        assertEquals(t, e.nowMs());
        e.setPaused(false);
        e.step();
        assertEquals(t + MatchEngine.TICK_MS, e.nowMs());
    }

    @Test
    void reviveCostsGoldAndIsLimitedToTwo() {
        MatchEngine e = engine(7, 1, List.of(member(1, "EARTH", 1, true, Map.of(0, 1), 1, 1)), 5);
        Entities.Fighter me = e.controlledFighter();
        assertFalse(e.revive(), "nothing to revive while alive");
        me.alive = false;
        me.hp = 0;
        assertTrue(e.revive());
        assertEquals(me.hpMax, me.hp);
        me.alive = false;
        assertTrue(e.revive());
        me.alive = false;
        assertFalse(e.revive(), "at most 2 revives per match");
        assertEquals(2, e.revivesUsed());
        // 0.5 win then 1.0 win at 1000 gold per level and level 1: 500 + 1000
        assertEquals(1500, e.outcome().reviveGoldSpent());
    }

    @Test
    void shieldAbsorbsAndStunOnABossIsShortened() {
        MatchEngine e = engine(12, 2, List.of(member(1, "EARTH", 100, true, Map.of(0, 7, 6, 7, 19, 7), 1, 5)), 9);
        Entities.Fighter me = e.controlledFighter();
        for (int i = 0; i < 80; i++) {
            e.step();
        }
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        // Khiên năng lượng (id 19, slot 7): the shield makes the character immune
        me.skillBySlot.put(7, d.skills().get(19));
        me.skillLevelBySlot.put(7, 7);
        me.ki = me.kiMax;
        assertTrue(e.useSkill(1, 7, 0, 0, 0));
        assertTrue(me.shielded(e.nowMs()));
        long before = me.hp;
        boss.x = me.x + 10;
        boss.y = me.y;
        for (int i = 0; i < 60; i++) {
            e.step();
        }
        assertEquals(before, me.hp, "a shielded character takes no damage");
        // Thái Dương Hạ San (id 6, slot 2) stuns the boss, but only for a fraction of the time (control factor 0.3, at most 3 s)
        me.skillBySlot.put(2, d.skills().get(6));
        me.skillLevelBySlot.put(2, 7);
        me.ki = me.kiMax;
        me.shieldUntil = 0;
        boss.x = me.x + 20;
        boss.y = me.y;
        long t0 = e.nowMs();
        assertTrue(e.useSkill(2, 2, 0, 0, 0));
        long stunMs = boss.stunUntil - t0;
        assertTrue(stunMs > 0 && stunMs <= 3000, "stun on a boss is capped, was " + stunMs);
        assertEquals(Math.min(3000, Math.round(9000 * 0.3)), stunMs, "level 7: 9000 ms x 0.3, capped at 3000");
    }

    @Test
    void minionsSwarmDuringTheCellFightAndCountTowardTheCap() {
        // dungeon 6 is the Cell group: bosses plus seven "Xên con" (elite minions)
        MatchEngine e = engine(6, 1, List.of(member(1, "EARTH", 1, true, Map.of(0, 1), 0.001, 200), member(2, "NAMEC", 1, false, Map.of(2, 1), 0.001, 200)), 21);
        int sawMinions = 0;
        for (int i = 0; i < 20 * 60 && !e.finished(); i++) {
            e.step();
            MatchFrame f = e.buildFrame();
            sawMinions += (int) f.getSpawnsList().stream().filter(s -> s.getKind() == com.battleball.proto.EntityKind.MINION).count();
        }
        assertTrue(sawMinions > 0, "elite minions must appear while the boss fights");
        assertTrue(sawMinions <= 20, "never more than minion_cap_per_match (20)");
    }

    @Test
    void framesCarryStatesDamageAndAck() {
        MatchEngine e = engine(12, 1, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 3)), 4);
        e.submit(() -> e.reportPosition(77, 300, 400, 1, 0));
        e.step();
        MatchFrame f = e.buildFrame();
        assertEquals(77, f.getAckSeq());
        assertNotNull(f.getStatesList().stream().filter(s -> s.getX() == 300f && s.getY() == 400f).findFirst().orElse(null));
        assertEquals(1, f.getServerTick());
    }

    @Test
    void experienceFactorsFollowTheDesign() {
        MatchEngine e = engine(12, 3, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS)), 2);
        // dungeon 12 is early tier, recommended level 15: no penalty up to level 20, then 5% less per level, floor 10%
        java.lang.reflect.Method m;
        try {
            m = MatchEngine.class.getDeclaredMethod("overlevelFactor", int.class);
            m.setAccessible(true);
            assertEquals(1.0, (double) m.invoke(e, 15));
            assertEquals(1.0, (double) m.invoke(e, 20));
            assertEquals(0.75, (double) m.invoke(e, 25), 1e-9);
            assertEquals(0.1, (double) m.invoke(e, 100), 1e-9);
            assertEquals(1.0, (double) m.invoke(e, 5), 1e-9);               // 10 below: no bonus yet
            assertEquals(1.1, (double) m.invoke(e, 0), 1e-9);               // 15 below: +5 x 2%
        } catch (ReflectiveOperationException ex) {
            throw new AssertionError(ex);
        }
    }

    @Test
    void damageMatchesTheDesignFormula() {
        // docs 7.4: base = ATK x skill %, x(1 - armor reduction of the target); here crit rate 0 so only the +/-5% spread remains
        StatCalculator.CharacterStats s = StatCalculator.compute(d, 100, List.of(), List.of());
        StatCalculator.CharacterStats noCrit = new StatCalculator.CharacterStats(s.hpMax() * 100, s.kiMax(), s.atk(), s.armor(), 0, s.critDmgPct(), 0, 0.5, 5, Map.of());
        MatchSetup.SquadMember m = new MatchSetup.SquadMember(1, "x", "EARTH", 100, true, false, 0, noCrit, Map.of(0, 7), 0, null);
        MatchEngine e = engine(12, 1, List.of(m), 13);
        for (int i = 0; i < 80; i++) {
            e.step();
        }
        Entities.Fighter me = e.controlledFighter();
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        me.x = boss.x - 30;                 // walk to the boss (moving the boss away from its home would make it leash back)
        me.y = boss.y;
        boss.shieldUntil = 0;
        List<Long> hits = new ArrayList<>();
        for (int i = 0; i < 400 && hits.size() < 40; i++) {
            e.useSkill(i, 0, boss.id, 1, 0);
            e.step();
            for (DamageEvent ev : e.buildFrame().getDamagesList()) {
                if (ev.getSourceId() == me.id && ev.getAmount() > 0) {
                    hits.add(ev.getAmount());
                }
            }
        }
        assertTrue(hits.size() >= 20, "the punch must hit the boss repeatedly");
        double expected = s.atk() * 1.60 * (1 - boss.armorReduction);                                   // punch level 7 = 160% ATK
        double avg = hits.stream().mapToLong(Long::longValue).average().orElse(0);
        assertEquals(expected, avg, expected * 0.04, "average punch damage = ATK x 160% x (1 - boss armor)");
        assertTrue(hits.stream().allMatch(h -> h >= expected * 0.94 && h <= expected * 1.06), "the spread stays inside +/-5%");
    }

    // ---- training in the starter village (docs/architecture/BattleBallServer.md 15.12) ----
    private static MatchEngine training(long idleEndMs, long dummyHp) {
        List<MatchSetup.SquadMember> squad = List.of(member(1, "EARTH", 30, true, EARTH_SKILLS, 300, 1), member(2, "NAMEC", 30, false, NAMEC_SKILLS, 300, 1),
                member(3, "XAYDA", 30, false, XAYDA_SKILLS, 300, 1));
        MatchSetup.Training tr = new MatchSetup.Training(3000, 800, List.of(new MatchSetup.Dummy("moc_nhan", "Mộc nhân", dummyHp, 0, 0, 24, 0, 1000, 300)),
                2000, idleEndMs);
        return new MatchEngine(new MatchSetup(0, d, null, squad, List.of(), List.of(), 0, new Random(3), tr));
    }

    @Test
    void aTrainingDummyRefillsItsHpAndTrainingNeverEndsByItself() {
        MatchEngine e = training(600_000, 3_000_000);
        MatchFrame first = e.buildFrame();
        Entities.Dummy dummy = (Entities.Dummy) e.entities().stream().filter(x -> x instanceof Entities.Dummy).findFirst().orElseThrow();
        assertTrue(first.getSpawnsList().stream().anyMatch(s -> s.getEntityId() == dummy.id && s.getKind() == com.battleball.proto.EntityKind.TRAINING_DUMMY
                && s.getDummyKey().equals("moc_nhan") && s.getMobTemplateId() == 0 && s.getHpMax() == 3_000_000));
        Entities.Fighter me = e.controlledFighter();
        List<Entities.Fighter> allies = e.squad().stream().filter(f -> !f.controlled).toList();
        long dealtByMe = 0;
        long dealtByAllies = 0;
        boolean refilled = false;
        for (int i = 0; i < 20 * 120; i++) {                       // two simulated minutes
            int seq = i;
            e.submit(() -> {
                e.reportPosition(seq, dummy.x - 30, dummy.y, 1, 0);
                e.reportAlly(allies.get(0).id, dummy.x + 30, dummy.y, -1, 0);
                e.reportAlly(allies.get(1).id, dummy.x - 32, dummy.y + 4, 1, 0);
                e.useSkill(seq, 0, dummy.id, 1, 0);
            });
            e.step();
            MatchFrame f = e.buildFrame();
            for (DamageEvent ev : f.getDamagesList()) {
                if (ev.getTargetId() == dummy.id) {
                    if (ev.getSourceId() == me.id) {
                        dealtByMe += ev.getAmount();
                    } else {
                        dealtByAllies += ev.getAmount();
                    }
                }
            }
            refilled |= f.getEffectsList().stream().anyMatch(ef -> ef.getEffect() == Effect.REGEN && ef.getTargetId() == dummy.id);
            assertTrue(dummy.alive, "a training dummy never dies");
            assertTrue(dummy.hp > 0 && dummy.hp <= dummy.hpMax);
        }
        assertFalse(e.finished(), "training does not end by time or by defeat");
        assertTrue(dealtByMe > 0, "the controlled character hits the dummy");
        assertTrue(dealtByAllies > 0, "the AI characters standing next to it hit the dummy too");
        assertTrue(dealtByMe + dealtByAllies > dummy.hpMax && refilled, "the dummy got its HP back after running out");
        assertEquals(dummy.x + 30, allies.get(0).x, 1e-6, "an AI character stays where the client puts it");
    }

    @Test
    void aCooldownIsKeptAndCarriedToTheNextSession() {
        MatchEngine e = training(600_000, 3_000_000);
        Entities.Dummy dummy = (Entities.Dummy) e.entities().stream().filter(x -> x instanceof Entities.Dummy).findFirst().orElseThrow();
        Entities.Fighter me = e.controlledFighter();
        e.submit(() -> {
            e.reportPosition(1, dummy.x - 30, dummy.y, 1, 0);
            e.useSkill(1, 0, dummy.id, 1, 0);
        });
        e.step();
        long left = e.cooldownsLeft().getOrDefault(me.characterId, java.util.Map.of()).getOrDefault(me.skillBySlot.get(0).id(), 0L);
        assertTrue(left > 0, "the punch is cooling down after use");
        MatchEngine next = training(600_000, 3_000_000);
        Entities.Fighter again = next.controlledFighter();
        assertEquals(0, next.cooldownLeftMs(again, 0), "a new session starts with no cooldown");
        long wall = 1_000_000_000L;
        next.applyCooldowns(java.util.Map.of(again.characterId, java.util.Map.of(again.skillBySlot.get(0).id(), wall + 4000)), wall);
        assertEquals(4000, next.cooldownLeftMs(again, 0), "the cooldown kept from the last session (real clock) is still running");
        next.submit(() -> {
            next.reportPosition(1, 0, 0, 1, 0);
            next.useSkill(1, 0, 0, 1, 0);
        });
        next.step();
        assertTrue(next.cooldownLeftMs(again, 0) > 3000, "the skill cannot be used while the kept cooldown runs");
    }

    @Test
    void trainingEndsAfterTheIdleTime() {
        MatchEngine e = training(2000, 2_000_000_000L);
        for (int i = 0; i < 20 * 3 && !e.finished(); i++) {
            e.step();
            e.buildFrame();
        }
        assertTrue(e.finished());
        assertEquals(MatchResult.ABANDONED, e.outcome().result());
    }
}
