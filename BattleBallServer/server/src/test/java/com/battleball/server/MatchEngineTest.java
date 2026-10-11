package com.battleball.server;

import com.battleball.proto.DamageEvent;
import com.battleball.proto.Effect;
import com.battleball.proto.EffectEvent;
import com.battleball.proto.EntityKind;
import com.battleball.proto.EntitySpawn;
import com.battleball.proto.MatchFrame;
import com.battleball.proto.EntityState;
import com.battleball.proto.MatchResult;
import com.battleball.server.data.Json;
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
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
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

    /** A dungeon without small monsters: the boss comes after 3 seconds (the tests of one boss do not wait for the waves). */
    private static MatchEngine engineBare(int dungeonId, int bosses, List<MatchSetup.SquadMember> squad, long seed) {
        MatchSetup s = new MatchSetup(1, d, d.dungeons().get(dungeonId), squad, encounters(dungeonId, bosses), List.of(), 10_000_000, new Random(seed));
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
        MatchEngine e = engineBare(13, 2, List.of(member(1, "EARTH", 100, true, Map.of(0, 7), 0.0001, 40), member(2, "NAMEC", 100, false, Map.of(2, 7), 0.0001, 40)), 11);
        e.forcePlanet("EARTH");                                           // the area stun is a skill of the Earth planet
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
        MatchEngine e = engineBare(12, 2, List.of(member(1, "EARTH", 100, true, Map.of(0, 7, 6, 7, 19, 7), 1, 5)), 9);
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

    // ---- the stages of a dungeon (docs/architecture/BattleBallServer.md 15.25) ----

    /** What a whole run of two stages showed: the order of the waves and the boss, how many monsters there were, what the small monsters did. */
    private static final class StageLog {
        final List<String> order = new ArrayList<>();
        final List<Integer> waveWidths = new ArrayList<>();
        int wavesLength;
        int minionsBeforeFirstBoss;
        int maxAliveBeforeFirstBoss;
        int aliveAtFirstBoss = -1;
        int summonsOfFirstBoss;
        int maxSummonedAlive;
        int maxMinionShots;
        final Set<Integer> minionDamageSkills = new HashSet<>();
        final Set<Integer> minionShotSkills = new HashSet<>();
        final Set<String> bossPlanets = new HashSet<>();
        boolean bossComingSeen;
        int deathsOfMinions;
        MatchOutcome outcome;
    }

    private static StageLog stageLog;

    private static StageLog stages() {
        if (stageLog != null) {
            return stageLog;
        }
        StageLog log = new StageLog();
        MatchEngine e = engine(12, 2, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 60), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 60),
                member(3, "XAYDA", 100, false, XAYDA_SKILLS, 8, 60)), 7);
        Map<Integer, EntitySpawn> known = new HashMap<>();
        Set<Integer> aliveMinions = new HashSet<>();
        Set<Integer> aliveSummoned = new HashSet<>();
        int bosses = 0;
        long firstBossTick = 0;
        for (int i = 0; i < 20 * 1500 && !e.finished(); i++) {
            play(e);
            e.step();
            MatchFrame f = e.buildFrame();
            for (EffectEvent ev : f.getEffectsList()) {
                if (ev.getEffect() == Effect.WAVE) {
                    log.order.add("W" + ev.getSkillId());
                    if (bosses == 0) {
                        log.waveWidths.add((int) ev.getWidth());
                        log.wavesLength = (int) ev.getLength();
                    }
                } else if (ev.getEffect() == Effect.BOSS_COMING) {
                    log.bossComingSeen = true;
                } else if (ev.getEffect() == Effect.SUMMON && bosses == 1) {
                    log.summonsOfFirstBoss++;
                } else if (ev.getEffect() == Effect.DIED && known.containsKey((int) ev.getTargetId()) && known.get((int) ev.getTargetId()).getKind() == EntityKind.MINION) {
                    log.deathsOfMinions++;
                }
            }
            for (EntitySpawn sp : f.getSpawnsList()) {
                known.put(sp.getEntityId(), sp);
                if (sp.getKind() == EntityKind.MINION) {
                    aliveMinions.add(sp.getEntityId());
                    if (sp.getSummoned()) {
                        aliveSummoned.add(sp.getEntityId());
                    } else if (bosses == 0) {
                        log.minionsBeforeFirstBoss++;
                    }
                } else if (sp.getKind() == EntityKind.BOSS) {
                    bosses++;
                    log.order.add("B");
                    log.bossPlanets.add(sp.getPlanet());
                    if (bosses == 1) {
                        log.aliveAtFirstBoss = (int) aliveMinions.stream().filter(id -> !aliveSummoned.contains(id)).count();
                    }
                }
            }
            for (var st : f.getStatesList()) {
                if (!st.getAlive()) {
                    aliveMinions.remove(st.getEntityId());
                    aliveSummoned.remove(st.getEntityId());
                }
            }
            for (int id : f.getDespawnsList()) {
                aliveMinions.remove(id);
                aliveSummoned.remove(id);
            }
            if (bosses == 0) {
                log.maxAliveBeforeFirstBoss = Math.max(log.maxAliveBeforeFirstBoss, aliveMinions.size());
            }
            log.maxSummonedAlive = Math.max(log.maxSummonedAlive, aliveSummoned.size());
            int shots = 0;
            for (var pr : f.getProjectilesList()) {
                EntitySpawn owner = known.get(pr.getOwnerId());
                if (owner != null && owner.getKind() == EntityKind.MINION) {
                    shots++;
                    log.minionShotSkills.add(pr.getSkillId());
                }
            }
            log.maxMinionShots = Math.max(log.maxMinionShots, shots);
            for (DamageEvent dm : f.getDamagesList()) {
                EntitySpawn src = known.get(dm.getSourceId());
                if (src != null && src.getKind() == EntityKind.MINION) {
                    log.minionDamageSkills.add(dm.getSkillId());
                }
            }
        }
        log.outcome = e.outcome();
        stageLog = log;
        return log;
    }

    @Test
    void everyBossIsPrecededByFiveWavesOfSmallMonsters() {
        StageLog log = stages();
        assertEquals(MatchResult.WON, log.outcome.result(), "the strong squad wins the two stages");
        assertEquals(List.of(12, 15, 18, 20, 25), log.waveWidths, "the sizes of the waves");
        assertEquals(5, log.wavesLength);
        int firstBoss = log.order.indexOf("B");
        assertEquals(List.of("W1", "W2", "W3", "W4", "W5"), log.order.subList(0, firstBoss), "five waves, then the boss");
        assertEquals(90, log.minionsBeforeFirstBoss, "12 + 15 + 18 + 20 + 25 small monsters in a stage");
        assertEquals(0, log.aliveAtFirstBoss, "the boss comes only when the last small monster has fallen");
        assertTrue(log.maxAliveBeforeFirstBoss <= 30, "never more than 30 alive at once, was " + log.maxAliveBeforeFirstBoss);
        assertTrue(log.maxAliveBeforeFirstBoss >= 12, "the waves really rush in, was " + log.maxAliveBeforeFirstBoss);
        int secondStage = log.order.subList(firstBoss + 1, log.order.size()).indexOf("W1");
        assertTrue(secondStage >= 0, "the second boss has its own waves");
        assertTrue(log.bossComingSeen);
    }

    @Test
    void aBossCallsSmallMonstersWhileItFights() {
        StageLog log = stages();
        assertTrue(log.summonsOfFirstBoss >= 5, "five when it appears, then three at a time: " + log.summonsOfFirstBoss);
        assertTrue(log.summonsOfFirstBoss <= 20, "at most 20 for one boss");
        assertTrue(log.maxSummonedAlive <= 8, "at most 8 called ones alive, was " + log.maxSummonedAlive);
    }

    @Test
    void smallMonstersPunchAndShootAChuongOfLevelOne() {
        StageLog log = stages();
        assertTrue(log.minionShotSkills.contains(5), "the Halloween monsters are Xayda: Antomic, " + log.minionShotSkills);
        assertTrue(log.minionDamageSkills.contains(4), "and Galick punches, " + log.minionDamageSkills);
        assertTrue(log.maxMinionShots <= 6, "never more than 6 shots in the air, was " + log.maxMinionShots);
        assertTrue(log.maxMinionShots >= 2, "they do shoot together, was " + log.maxMinionShots);
        assertTrue(log.deathsOfMinions > 50, "the deaths of the small monsters are announced: " + log.deathsOfMinions);
    }

    @Test
    void everyBossDrawsAPlanetAndTheMinionsOfTheMatchStayUnderTheCap() {
        StageLog log = stages();
        assertTrue(log.bossPlanets.stream().allMatch(pl -> pl.equals("EARTH") || pl.equals("NAMEC") || pl.equals("XAYDA")), "planets: " + log.bossPlanets);
        long minionKills = log.outcome.kills().stream().filter(k -> !k.realBoss()).count();
        assertTrue(minionKills <= d.balanceNum("minion_cap_per_match", 20), "a kill for each wave monster, never above the cap: " + minionKills);
        assertEquals(180, minionKills, "two stages of 90 (the called monsters pay nothing)");
    }

    /** Waits until the first wave is in and stuns every small monster for a minute, so that the test places them where it wants. */
    private static List<Entities.Monster> waveMinionsFrozen(MatchEngine e) {
        for (int i = 0; i < 20 * 12; i++) {
            e.step();
            e.buildFrame();
            if (e.entities().stream().filter(x -> x instanceof Entities.Monster).count() >= 12) {
                break;
            }
        }
        List<Entities.Monster> out = new ArrayList<>();
        for (Entities.Entity x : e.entities()) {
            if (x instanceof Entities.Monster m) {
                m.stunUntil = e.nowMs() + 600_000;
                m.x = 1700;
                m.y = 500;
                out.add(m);
            }
        }
        assertTrue(out.size() >= 12, "the first wave of 12 is in");
        return out;
    }

    private static MatchEngine strongEngine(Map<Integer, Integer> skills, long seed) {
        return engine(12, 1, List.of(member(1, "EARTH", 100, true, skills, 8, 60), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 60)), seed);
    }

    @Test
    void theChuongOfASkillTwoFliesToTheEdgeOfTheScreenThroughEveryone() {
        MatchEngine e = strongEngine(Map.of(0, 7, 1, 7), 31);
        Entities.Fighter me = e.controlledFighter();
        List<Entities.Monster> ms = waveMinionsFrozen(e);
        me.x = 100;
        me.y = 260;
        e.submit(() -> e.reportView(0, 100, 640, 360));
        double[] xs = {250, 450, 600, 900};                                    // the last one is outside the screen (it ends at 640)
        for (int i = 0; i < xs.length; i++) {
            ms.get(i).x = xs[i];
            ms.get(i).y = 260;
            ms.get(i).hp = ms.get(i).hpMax = 1_000_000_000L;
        }
        me.skillBySlot.put(1, d.skills().get(1));
        me.skillLevelBySlot.put(1, 7);
        me.ki = me.kiMax;
        e.step();
        assertTrue(e.useSkill(1, 1, ms.get(0).id, 1, 0));
        Set<Integer> hit = new HashSet<>();
        for (int i = 0; i < 60; i++) {
            e.step();
            for (DamageEvent dm : e.buildFrame().getDamagesList()) {
                if (dm.getSourceId() == me.id && dm.getSkillId() == 1 && dm.getAmount() > 0) {
                    hit.add(dm.getTargetId());
                }
            }
        }
        assertTrue(hit.contains(ms.get(0).id) && hit.contains(ms.get(1).id), "the one in focus and the one behind it");
        assertTrue(hit.contains(ms.get(2).id), "and the third one, farther than the 300 px of the skill, still on the screen");
        assertFalse(hit.contains(ms.get(3).id), "but not the one outside the screen");
    }

    @Test
    void theAiFightsOnlyWhatIsOnTheScreenAndDoesNotFollowThePlayer() {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 60), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 60)), 5);
        e.submit(() -> e.reportView(0, 0, 640, 360));
        for (int i = 0; i < 70; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter ai = e.squad().get(1);
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.x = 1500;
        boss.y = 260;
        boss.homeX = 1500;
        boss.homeY = 260;
        boss.stunUntil = e.nowMs() + 600_000;
        double ax = ai.x;
        double ay = ai.y;
        for (int i = 0; i < 60; i++) {
            e.step();
            e.buildFrame();
        }
        assertEquals(ax, ai.x, 1e-6, "nothing on the screen: the AI stands where it is");
        assertEquals(ay, ai.y, 1e-6);
        e.submit(() -> e.reportView(1000, 0, 700, 360));                      // the screen now shows the boss
        for (int i = 0; i < 40; i++) {
            e.step();
            e.buildFrame();
        }
        assertTrue(ai.x > ax + 100, "the boss is on the screen: the AI goes for it, x " + ax + " -> " + ai.x);
    }

    @Test
    void anAiCharacterDefendsItselfAgainstWhatIsNextToItEvenOffTheScreen() {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 60), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 60)), 6);
        e.submit(() -> e.reportView(1000, 0, 700, 360));                   // the screen is far from the squad
        for (int i = 0; i < 70; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter ai = e.squad().get(1);
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.x = ai.x + 100;
        boss.y = ai.y;
        boss.homeX = boss.x;
        boss.homeY = boss.y;
        boss.stunUntil = e.nowMs() + 600_000;
        boss.shieldUntil = 0;
        boolean struck = false;
        for (int i = 0; i < 60 && !struck; i++) {
            e.step();
            for (DamageEvent dm : e.buildFrame().getDamagesList()) {
                struck |= dm.getSourceId() == ai.id && dm.getTargetId() == boss.id && dm.getAmount() > 0;
            }
        }
        assertTrue(struck, "an enemy within 220 px is fought even when it is not on the screen");
    }

    @Test
    void anUltimateTurnsOnceAroundAgainstSmallMonstersAndStaysStraightAgainstABoss() {
        // small monsters all around the caster, the one at 0 degrees in focus: every one of them is hit while the beam turns for 4 seconds
        MatchEngine e = strongEngine(Map.of(0, 7, 24, 9), 33);
        Entities.Fighter me = e.controlledFighter();
        List<Entities.Monster> ms = waveMinionsFrozen(e);
        me.x = 800;
        me.y = 260;
        e.submit(() -> e.reportView(480, 100, 640, 360));
        double[][] at = {{150, 0}, {0, 150}, {-150, 0}, {0, -150}, {250, 120}, {-250, -100}};
        for (int i = 0; i < at.length; i++) {
            ms.get(i).x = me.x + at[i][0];
            ms.get(i).y = me.y + at[i][1];
            ms.get(i).hp = ms.get(i).hpMax = 1_000_000_000L;
        }
        me.skillBySlot.put(8, d.skills().get(24));
        me.skillLevelBySlot.put(8, 9);
        me.ki = me.kiMax;
        e.step();
        assertTrue(e.useSkill(1, 8, ms.get(0).id, 1, 0));
        Map<Integer, Integer> hits = new HashMap<>();
        boolean spinning = false;
        long beamMs = 0;
        for (int i = 0; i < 20 * 7; i++) {
            e.step();
            MatchFrame f = e.buildFrame();
            for (EffectEvent ev : f.getEffectsList()) {
                if (ev.getEffect() == Effect.ULTIMATE_BEAM) {
                    spinning = ev.getSpinDegPerS() > 0;
                    beamMs = ev.getDurationMs();
                    assertEquals(90.0, ev.getSpinDegPerS(), 0.5, "one turn in 4 seconds");
                }
            }
            for (DamageEvent dm : f.getDamagesList()) {
                if (dm.getSourceId() == me.id && dm.getSkillId() == 24 && dm.getAmount() > 0) {
                    hits.merge(dm.getTargetId(), 1, Integer::sum);
                }
            }
        }
        assertTrue(spinning);
        assertEquals(4000, beamMs);
        for (int i = 0; i < at.length; i++) {
            assertTrue(hits.getOrDefault(ms.get(i).id, 0) >= 1, "the small monster " + i + " at " + at[i][0] + "," + at[i][1] + " is hit by the turning beam: " + hits);
        }
        // against a boss in focus the beam is straight and lasts the usual 2 seconds
        MatchEngine b = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, Map.of(0, 7, 24, 9), 8, 60)), 34);
        for (int i = 0; i < 70; i++) {
            b.step();
            b.buildFrame();
        }
        Entities.Fighter hero = b.controlledFighter();
        Entities.Monster boss = (Entities.Monster) b.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.stunUntil = b.nowMs() + 600_000;
        hero.skillBySlot.put(8, d.skills().get(24));
        hero.skillLevelBySlot.put(8, 9);
        hero.ki = hero.kiMax;
        hero.x = boss.x - 200;
        hero.y = boss.y;
        assertTrue(b.useSkill(1, 8, boss.id, 1, 0));
        float spin = -1;
        long straightMs = 0;
        for (int i = 0; i < 20 * 3; i++) {
            b.step();
            for (EffectEvent ev : b.buildFrame().getEffectsList()) {
                if (ev.getEffect() == Effect.ULTIMATE_BEAM) {
                    spin = ev.getSpinDegPerS();
                    straightMs = ev.getDurationMs();
                }
            }
        }
        assertEquals(0f, spin, "a beam on a boss does not turn");
        assertEquals(2000, straightMs);
    }

    @Test
    void aBossFightsWithTheSkillsOfThePlanetItDrew() {
        for (String planet : new String[] {"EARTH", "NAMEC", "XAYDA"}) {
            MatchEngine e = engineBare(13, 1, List.of(member(1, "EARTH", 100, true, Map.of(0, 7), 0.0001, 4000), member(2, "NAMEC", 100, false, Map.of(2, 7), 0.0001, 4000)), 41);
            e.forcePlanet(planet);
            Set<Integer> skills = new HashSet<>();
            Set<Integer> shots = new HashSet<>();
            for (int i = 0; i < 20 * 150 && !e.finished(); i++) {
                e.step();
                MatchFrame f = e.buildFrame();
                for (DamageEvent dm : f.getDamagesList()) {
                    if (dm.getSourceId() != e.controlledFighter().id && e.squad().stream().noneMatch(q -> q.id == dm.getSourceId())) {
                        skills.add(dm.getSkillId());
                    }
                }
                f.getProjectilesList().forEach(pr -> shots.add(pr.getSkillId()));
            }
            Set<Integer> allowed = switch (planet) {
                case "EARTH" -> Set.of(0, 1, 6, 10, 24);
                case "NAMEC" -> Set.of(2, 3, 11);
                default -> Set.of(4, 5, 25);
            };
            assertFalse(skills.isEmpty(), planet + ": the boss hurt somebody");
            assertTrue(allowed.containsAll(skills), planet + " uses only its own skills, used " + skills);
            assertTrue(allowed.containsAll(shots), planet + " shoots only its own chuong, shot " + shots);
            if (planet.equals("EARTH")) {
                assertTrue(skills.contains(6) || skills.contains(10), "an Earth boss has the area stun and the bomb: " + skills);
            } else {
                assertFalse(skills.contains(6) || skills.contains(10), planet + " has neither the Earth stun nor the bomb: " + skills);
            }
        }
    }

    @Test
    void aDeathIsAnnouncedWithWhatKilledIt() {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, Map.of(0, 7), 8, 60)), 51);
        for (int i = 0; i < 70; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter me = e.controlledFighter();
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.shieldUntil = 0;
        boss.hp = 1;
        me.x = boss.x - 30;
        me.y = boss.y;
        assertTrue(e.useSkill(1, 0, boss.id, 1, 0));
        EffectEvent died = null;
        for (int i = 0; i < 6 && died == null; i++) {
            e.step();
            for (EffectEvent ev : e.buildFrame().getEffectsList()) {
                if (ev.getEffect() == Effect.DIED && ev.getTargetId() == boss.id) {
                    died = ev;
                }
            }
        }
        assertNotNull(died, "the death of the boss is an event");
        assertEquals(me.id, died.getSourceId());
        assertTrue(died.getDirX() > 0.9f, "the blow came from the left, so the body is thrown to the right: " + died.getDirX());
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
        MatchEngine e = engineBare(12, 1, List.of(m), 13);
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

    /** A fighter whose GEN sets add these numbers to its skills (docs 15.21), crit rate 0 so only the +/-5% spread remains. */
    private static MatchEngine genFight(Map<String, Double> gen) {
        StatCalculator.CharacterStats s = StatCalculator.compute(d, 100, List.of(), List.of());
        StatCalculator.CharacterStats st = new StatCalculator.CharacterStats(s.hpMax() * 100, s.kiMax(), s.atk(), s.armor(), 0, s.critDmgPct(), 0, 0.5, 5, gen);
        MatchSetup.SquadMember m = new MatchSetup.SquadMember(1, "x", "EARTH", 100, true, false, 0, st, Map.of(0, 7), 0, null);
        MatchEngine e = engineBare(12, 1, List.of(m), 13);
        for (int i = 0; i < 80; i++) {
            e.step();
        }
        return e;
    }

    @Test
    void aGenSetAddsToTheDamageAndShortensTheCooldownOfOneSkillOnly() {
        double plain = 0, boosted = 0;
        long cooldownPlain = 0, cooldownBoosted = 0;
        for (int round = 0; round < 2; round++) {
            Map<String, Double> gen = round == 0 ? Map.of() : Map.of("SKILL_DMG_PCT:0", 100.0, "SKILL_COOLDOWN_PCT:0", 50.0, "SKILL_DMG_PCT:1", 500.0);
            MatchEngine e = genFight(gen);
            Entities.Fighter me = e.controlledFighter();
            Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
            me.x = boss.x - 30;
            me.y = boss.y;
            boss.shieldUntil = 0;
            List<Long> hits = new ArrayList<>();
            long cooldown = 0;
            for (int i = 0; i < 400 && hits.size() < 30; i++) {
                if (e.useSkill(i, 0, boss.id, 1, 0) && cooldown == 0) {
                    cooldown = e.cooldownLeftMs(me, 0);
                }
                e.step();
                for (DamageEvent ev : e.buildFrame().getDamagesList()) {
                    if (ev.getSourceId() == me.id && ev.getAmount() > 0) {
                        hits.add(ev.getAmount());
                    }
                }
            }
            assertTrue(hits.size() >= 20, "the punch hits the boss repeatedly");
            double avg = hits.stream().mapToLong(Long::longValue).average().orElse(0);
            if (round == 0) {
                plain = avg;
                cooldownPlain = cooldown;
            } else {
                boosted = avg;
                cooldownBoosted = cooldown;
            }
        }
        assertEquals(plain * 2, boosted, plain * 0.08, "+100% to the damage of the punch (the +500% of Kamejoko does not touch it)");
        assertEquals(cooldownPlain / 2.0, cooldownBoosted, cooldownPlain * 0.05 + 60, "50% shorter cooldown of the punch");
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

    // ---- the skills as the original game does them (docs/architecture/BattleBallServer.md 15.17) ----
    private static int slotOf(int skillId) {
        return d.skills().get(skillId).slot();
    }

    private static MatchSetup.Dummy dummy(String key, double x, double y) {
        return new MatchSetup.Dummy(key, key, 2_000_000_000L, 0, 0, 24, 0, x, y);
    }

    private static MatchEngine village(List<MatchSetup.SquadMember> squad, double arenaW, MatchSetup.Dummy... dummies) {
        MatchSetup.Training tr = new MatchSetup.Training(arenaW, 800, List.of(dummies), 2000, 600_000_000L);
        return new MatchEngine(new MatchSetup(0, d, null, squad, List.of(), List.of(), 0, new Random(5), tr));
    }

    /** The squad of the skill tests: the controlled character with these skills, two AI characters with none (they only stand). */
    private static List<MatchSetup.SquadMember> soloSquad(String race, Map<Integer, Integer> skills) {
        return List.of(member(1, race, 30, true, skills, 300, 1), member(2, "NAMEC", 30, false, Map.of(), 300, 1), member(3, "XAYDA", 30, false, Map.of(), 300, 1));
    }

    private static Entities.Dummy dummyOf(MatchEngine e, String key) {
        return (Entities.Dummy) e.entities().stream().filter(x -> x instanceof Entities.Dummy dm && dm.key.equals(key)).findFirst().orElseThrow();
    }

    /** Steps {@code ticks} ticks, every one after {@code each} ran; returns the frames in order. */
    private static List<MatchFrame> steps(MatchEngine e, int ticks, java.util.function.IntConsumer each) {
        List<MatchFrame> out = new ArrayList<>();
        for (int i = 0; i < ticks; i++) {
            if (each != null) {
                each.accept(i);
            }
            e.step();
            out.add(e.buildFrame());
        }
        return out;
    }

    private static int maskOf(MatchFrame f, int entityId) {
        return f.getStatesList().stream().filter(st -> st.getEntityId() == entityId).findFirst().orElseThrow().getEffects();
    }

    private static long hits(List<MatchFrame> frames, int skillId, int targetId) {
        return frames.stream().flatMap(f -> f.getDamagesList().stream()).filter(x -> x.getSkillId() == skillId && x.getTargetId() == targetId && x.getAmount() > 0).count();
    }

    private static long events(List<MatchFrame> frames, Effect effect, int skillId) {
        return frames.stream().flatMap(f -> f.getEffectsList().stream()).filter(x -> x.getEffect() == effect && x.getSkillId() == skillId).count();
    }

    @Test
    void skillsAreUsedOneAtATimeInOrder() {
        // Nro refuses a new skill while the paint of the last one plays or its dart still flies: a second skill in the same instant, or while the chưởng is in the air, is refused
        MatchEngine e = village(soloSquad("EARTH", Map.of(0, 1, 1, 1)), 5000, dummy("far", 1900, 300));
        Entities.Dummy far = dummyOf(e, "far");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        assertTrue(e.useSkill(2, slotOf(1), far.id, 1, 0), "the chưởng starts");
        assertFalse(e.useSkill(3, slotOf(0), far.id, 1, 0), "a second skill in the same instant is refused");
        steps(e, 6, null);                                                        // 300 ms
        assertFalse(e.useSkill(4, slotOf(0), far.id, 1, 0), "and so is one a moment later");
        steps(e, 5, null);                                                        // 550 ms: the gap (500 ms) is over but the chưởng (625 ms to the end of its way) is still flying
        assertFalse(e.useSkill(5, slotOf(0), far.id, 1, 0), "the next skill waits for the chưởng to end");
        steps(e, 6, null);                                                        // 850 ms
        assertTrue(e.useSkill(6, slotOf(0), far.id, 1, 0), "then it is the turn of the next skill");
        assertFalse(e.useSkill(7, slotOf(0), far.id, 1, 0), "and the one right after it has to wait again");
    }

    @Test
    void anUltimateBeamPiercesEveryTargetAndHitsOnEveryTickForTwoSeconds() {
        // Super Kamejoko: a gong of 2 s, then every 250 ms for 2 s everything in the rectangle (Nro NewSkill), up to 2 chunks (3360 px) away, the target in focus included
        MatchEngine e = village(soloSquad("EARTH", Map.of(24, 1)), 5000, dummy("a", 1400, 300), dummy("b", 3300, 300), dummy("off", 1800, 700));
        Entities.Fighter me = e.controlledFighter();
        Entities.Dummy a = dummyOf(e, "a"), b = dummyOf(e, "b"), off = dummyOf(e, "off");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(24), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 40 + 40 + 5, i -> e.submit(() -> e.reportPosition(10 + i, 1000 + 40, 300, 1, 0)));          // it tries to walk away all along
        assertEquals(1000, frames.get(76).getStatesList().stream().filter(st -> st.getEntityId() == me.id).findFirst().orElseThrow().getX(), 1e-6,
                "the caster stays where it started: the gong and the beam hold it");
        assertTrue((maskOf(frames.get(10), me.id) & 256) != 0, "the caster is busy during the gong");
        assertTrue((maskOf(frames.get(60), me.id) & 256) != 0, "and while the beam hits");
        assertEquals(7, hits(frames, 24, a.id), "the target in focus is hit on every tick (250 ms for 2 s)");
        assertEquals(7, hits(frames, 24, b.id), "and so is the one behind it, 2300 px away (the beam goes through)");
        assertEquals(0, hits(frames, 24, off.id), "a dummy outside the width of the beam is not hit");
        assertEquals(1, events(frames, Effect.ULTIMATE_BEAM, 24), "the client is told where the beam goes and for how long");
        assertTrue((maskOf(frames.get(frames.size() - 1), me.id) & 256) == 0, "the caster is free when the beam ends");
    }

    @Test
    void mafubaSuckedTargetsBecomeJarsAndTakeDamageWithoutDying() {
        // Ma phong ba: gong 2 s, within 500 px everything is sucked in; 4 s later each target is a jar for 11 s and takes the caster's max HP x level % every 500 ms
        MatchEngine e = village(soloSquad("NAMEC", Map.of(26, 1)), 5000, dummy("a", 1300, 300), dummy("b", 1400, 330), dummy("far", 2500, 300));
        Entities.Fighter me = e.controlledFighter();
        Entities.Dummy a = dummyOf(e, "a"), b = dummyOf(e, "b"), far = dummyOf(e, "far");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(26), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 40 + 80 + 230 + 10, null);
        assertEquals(2, events(frames, Effect.MAFUBA_GATHER, 26), "the two targets in range are sucked into the jar");
        assertEquals(2, events(frames, Effect.JAR, 26));
        assertTrue((maskOf(frames.get(40 + 40), a.id) & 1024) != 0, "while it is sucked in (the 4 s after the gong) it is marked so");
        assertTrue((maskOf(frames.get(40 + 80 + 40), a.id) & 512) != 0, "then it is a jar");
        assertTrue((maskOf(frames.get(40 + 80 + 40), far.id) & (512 | 1024)) == 0, "a target out of range is untouched");
        assertEquals(22, hits(frames, 26, a.id), "the jar hurts every 500 ms for 11 s");
        assertEquals(22, hits(frames, 26, b.id));
        assertEquals(0, hits(frames, 26, far.id));
        long expected = Math.round(me.hpMax * 1 * 1 / 100.0);
        long first = frames.stream().flatMap(f -> f.getDamagesList().stream()).filter(x -> x.getSkillId() == 26 && x.getTargetId() == a.id).findFirst().orElseThrow().getAmount();
        assertEquals(expected, first, "the damage is the caster's max HP x the level of the skill in percent");
        assertTrue((maskOf(frames.get(frames.size() - 1), a.id) & (512 | 1024)) == 0, "the jar is gone after 11 s");
    }

    @Test
    void theMonkeyHoldsOneAndAHalfSecondThenRaisesHpAttackAndCriticalRateForAWhile() {
        MatchEngine e = village(soloSquad("XAYDA", Map.of(13, 3)), 3000, dummy("a", 1400, 300));
        Entities.Fighter me = e.controlledFighter();
        StaticData.SkillLevel L = d.skills().get(13).level(3);
        double hpPct = Json.num(L.params(), "hpBonusPct", 0), atkPct = Json.num(L.params(), "dameBonusRaw", 0);
        long hpBefore = me.hpMax;
        double atkBefore = me.atk, critBefore = me.critRatePct;
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(13), 0, 1, 0));
        List<MatchFrame> frames = steps(e, 20, i -> e.submit(() -> e.reportPosition(10 + i, 1100, 300, 1, 0)));
        assertEquals(1000, me.x, 1e-6, "the hold of the transformation keeps the character where it is");
        assertEquals(hpBefore, me.hpMax, "the body changes only after the hold (1.5 s)");
        assertTrue((maskOf(frames.get(5), me.id) & 256) != 0);
        frames.addAll(steps(e, 20, null));
        assertEquals(hpBefore + Math.round(hpBefore * hpPct / 100.0), me.hpMax, "max HP + (level + 3) x 10 %");
        assertEquals(me.hpMax, me.hp, "the HP is doubled, up to the new maximum");
        assertEquals(atkBefore * (1 + atkPct / 100.0), me.atk, atkBefore * 1e-6, "attack + (level + 3) %");
        assertEquals(110, me.critRatePct, 1e-9, "the critical rate is 110 (always)");
        EntityState st = frames.get(frames.size() - 1).getStatesList().stream().filter(x -> x.getEntityId() == me.id).findFirst().orElseThrow();
        assertEquals(me.hpMax, st.getHpMax(), "the frame carries the live numbers for the menu");
        assertEquals(Math.round(me.atk), st.getAtk());
        assertEquals(100f, st.getCritRatePct(), 1e-3, "a rate above 100 counts as 100");
        assertTrue((st.getEffects() & 2048) != 0, "marked as a monkey");
        frames.addAll(steps(e, 20 * 82, null));                                                  // (3 + 5) x 10 s = 80 s
        assertEquals(hpBefore, me.hpMax, "everything is back when the monkey ends");
        assertEquals(atkBefore, me.atk, atkBefore * 1e-6);
        assertEquals(critBefore, me.critRatePct, 1e-9);
        assertTrue(events(frames, Effect.CAST_END, 13) >= 1, "the client is told the monkey ended");
    }

    @Test
    void theRegenerationEndsWhenTheCharacterMoves() {
        MatchEngine e = village(soloSquad("XAYDA", Map.of(8, 1)), 3000, dummy("a", 1900, 300));
        Entities.Fighter me = e.controlledFighter();
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        me.hp = me.hpMax / 2;
        me.ki = me.kiMax / 2;
        long hp0 = me.hp;
        double ki0 = me.ki;
        e.submit(() -> e.useSkill(2, slotOf(8), 0, 1, 0));
        List<MatchFrame> frames = steps(e, 40, i -> e.submit(() -> e.reportPosition(10 + i, 1000.4, 300, 1, 0)));          // standing (a rounding of the position is not a move)
        assertTrue((maskOf(frames.get(30), me.id) & 32) != 0, "regenerating");
        assertTrue(me.hp > hp0 && me.ki > ki0, "it heals the HP and the Ki (NPoint.update)");
        e.submit(() -> e.reportPosition(99, 1060, 300, 1, 0));
        frames = steps(e, 2, null);
        assertTrue((maskOf(frames.get(1), me.id) & 32) == 0, "a move ends the regeneration (PlayerService.playerMove: stopCharge)");
        assertEquals(1, events(frames, Effect.CAST_END, 8));
    }

    @Test
    void theRopeBreaksWhenTheHolderMoves() {
        MatchEngine e = village(soloSquad("XAYDA", Map.of(23, 1, 4, 1)), 3000, dummy("a", 1100, 300));
        Entities.Dummy a = dummyOf(e, "a");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(23), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 30, i -> e.submit(() -> e.reportPosition(10 + i, 1000, 300, 1, 0)));
        assertTrue((maskOf(frames.get(29), a.id) & 2) != 0, "held while the holder stands there");
        e.submit(() -> e.reportPosition(99, 1080, 300, 1, 0));
        frames = steps(e, 2, null);
        assertTrue((maskOf(frames.get(1), a.id) & 2) == 0, "it lets go when the holder moves (Nro removeUseTroi)");
        assertEquals(1, events(frames, Effect.CAST_END, 23));
    }

    @Test
    void theSpiritBombLandsOnTheTargetNotOnTheCaster() {
        // Quả cầu kênh khí: the circle is around the target the ball is thrown at (Nro: distance from plTarget / mobTarget), 4 s of charge
        MatchEngine e = village(soloSquad("EARTH", Map.of(10, 1)), 5000, dummy("a", 1300, 300), dummy("near", 1500, 300), dummy("far", 2600, 300));
        Entities.Fighter me = e.controlledFighter();
        Entities.Dummy a = dummyOf(e, "a"), near = dummyOf(e, "near"), far = dummyOf(e, "far");
        e.submit(() -> e.reportPosition(1, 500, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(10), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 4 * 20 + 5, null);
        assertEquals(1, hits(frames, 10, a.id), "the target is hit");
        assertEquals(1, hits(frames, 10, near.id), "and the one 200 px from it, although both are far from the caster (800 and 1000 px)");
        assertEquals(0, hits(frames, 10, far.id), "one outside the radius around the target is not");
        assertEquals(500, me.x, 1e-6);
    }

    @Test
    void theSleepAffectsOnlyTheTarget() {
        MatchEngine e = village(soloSquad("EARTH", Map.of(22, 1)), 3000, dummy("a", 1100, 300), dummy("next", 1150, 300));
        Entities.Dummy a = dummyOf(e, "a"), next = dummyOf(e, "next");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(22), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 5, null);
        assertTrue((maskOf(frames.get(4), a.id) & 4) != 0, "the target sleeps");
        assertTrue((maskOf(frames.get(4), next.id) & 4) == 0, "what stands next to it does not (Nro: Thôi miên has no area)");
    }

    @Test
    void theTeleportStrikesWithACriticalHitAndStunsForHalfASecondPerLevelPlusOne() {
        // Nro SkillService: setPos next to the target, playerAttackPlayer (always a critical hit), then the stun of (level + 1) x 500 ms
        MatchEngine e = village(soloSquad("EARTH", Map.of(20, 3)), 3000, dummy("a", 1900, 300));
        Entities.Dummy a = dummyOf(e, "a");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(20), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 5, null);
        var hit = frames.stream().flatMap(f -> f.getDamagesList().stream()).filter(x -> x.getSkillId() == 20).findFirst().orElseThrow();
        assertTrue(hit.getCrit() && hit.getAmount() > 0, "the teleport itself is a critical hit");
        assertTrue((maskOf(frames.get(4), a.id) & 1) != 0, "and the target is stunned");
        assertEquals(2000, Json.num(d.skills().get(20).level(3).params(), "stunMs", 0), 1e-9, "level 3: (3 + 1) x 500 ms");
    }

    @Test
    void makankosappoHitsOnlyTheTargetItWasAimedAt() {
        MatchEngine e = village(soloSquad("NAMEC", Map.of(11, 1)), 4000, dummy("a", 1400, 300), dummy("behind", 1900, 300));
        Entities.Dummy a = dummyOf(e, "a"), behind = dummyOf(e, "behind");
        e.submit(() -> e.reportPosition(1, 1000, 300, 1, 0));
        e.step();
        e.buildFrame();
        e.submit(() -> e.useSkill(2, slotOf(11), a.id, 1, 0));
        List<MatchFrame> frames = steps(e, 3 * 20 + 5, null);
        assertEquals(1, hits(frames, 11, a.id), "the target in focus is hit after the charge of 3 s");
        assertEquals(0, hits(frames, 11, behind.id), "what stands behind it is not (Nro: plTarget or mobTarget)");
    }

    @Test
    void takingOverAnotherCharacterResetsNothing() {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 60), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 8, 60)), 7);
        for (int i = 0; i < 10; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter first = e.squad().get(0);
        Entities.Fighter second = e.squad().get(1);
        second.hp = second.hpMax / 2;
        second.shieldUntil = e.nowMs() + 600_000;                       // a buff the switch must not touch
        long hp = second.hp;
        double x = second.x;
        assertSame(first, e.controlledFighter());
        e.submit(() -> assertSame(second, e.switchControl(second.id)));
        e.step();
        assertSame(second, e.controlledFighter());
        assertTrue(second.controlled);
        assertFalse(first.controlled, "the old one is AI now");
        assertTrue(second.hp >= hp && second.hp < second.hpMax, "HP kept (it only regenerates a little)");
        assertTrue(second.shieldUntil > e.nowMs(), "the buff is kept");
        assertEquals(x, second.x, 1e-6, "it stays where it stood");
        e.submit(() -> e.reportPosition(1, 700, 300, 1, 0));            // the player moves the new one: the report goes to it
        e.step();
        assertEquals(700, second.x, 1e-6);
        assertNull(e.switchControl(9999), "no such fighter");
    }

    @Test
    void noBossHasTheSelfDestructAndTheApeKitFollowsTheXaydaSkills() {
        java.util.Set<String> moveKeys = java.util.Set.of("punch", "beam", "ultimate", "stun", "bomb", "focus");
        if (d.balance().get("boss_planet_kits") instanceof Map<?, ?> kits) {
            for (Map.Entry<?, ?> kit : kits.entrySet()) {
                if (kit.getValue() instanceof Map<?, ?> k) {
                    for (Map.Entry<?, ?> e : k.entrySet()) {
                        String key = String.valueOf(e.getKey());
                        if (moveKeys.contains(key) && e.getValue() instanceof Number n) {
                            assertTrue(n.intValue() != 14, "the kit " + kit.getKey() + " has the Tu phat no as its " + key);
                        }
                        if (key.equals("cd_skills") && e.getValue() instanceof Map<?, ?> skills) {
                            skills.values().forEach(v -> assertTrue(!(v instanceof Number n) || n.intValue() != 14, "a move of the kit " + kit.getKey() + " is the Tu phat no"));
                        }
                    }
                }
            }
        }
        assertTrue(d.balance().get("boss_banned_skills") instanceof List<?> l && l.stream().anyMatch(o -> o instanceof Number n && n.intValue() == 14), "14 is banned for every boss");
        Map<?, ?> ape = (Map<?, ?>) ((Map<?, ?>) d.balance().get("boss_planet_kits")).get("APE");
        assertEquals(List.of("ultimate", "bind", "whistle", "regen", "beam", "punch"), ape.get("actions"));
        Map<?, ?> skills = (Map<?, ?>) ape.get("cd_skills");
        assertEquals(23, ((Number) skills.get("bind")).intValue());
        assertEquals(25, ((Number) skills.get("ultimate")).intValue());
    }

    @Test
    void theApeBossTurnsIntoItsNextFormWhenItsHpIsNearlyGoneAndPaysForEveryForm() {
        List<StaticData.Boss> forms = new ArrayList<>();
        for (int id = 78; id <= 84; id++) {
            forms.add(d.bosses().get(id));
        }
        MatchSetup setup = new MatchSetup(1, d, d.dungeons().get(3), List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 60)),
                List.of(new MatchSetup.Encounter(forms.get(0), "NORMAL", null, forms)), List.of(), 10_000_000, new Random(3));
        MatchEngine e = new MatchEngine(setup);
        assertEquals(7, e.bossCount(), "every form counts as a boss");
        for (int i = 0; i < 90; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        assertEquals(forms.get(0).name(), boss.name);
        assertEquals("XAYDA", boss.planet);
        for (int form = 0; form < 6; form++) {
            boss.stunUntil = e.nowMs() + 600_000;                                   // it does not fight back in this test
            boss.hp = Math.max(2, Math.round(boss.hpMax * 0.12));                   // just above the point where it evolves
            int guard = 0;
            while (boss.evolveUntil == 0 && guard++ < 400) {
                play(e);
                e.step();
                e.buildFrame();
            }
            assertTrue(boss.evolveUntil != 0, "the form " + (form + 1) + " starts to evolve");
            assertTrue(boss.alive && boss.hp >= 1, "it is not killed");
            long hpCharging = boss.hp;
            for (int i = 0; i < 20; i++) {                                         // the charge: nothing hurts it
                play(e);
                e.step();
                e.buildFrame();
            }
            assertEquals(hpCharging, boss.hp, "it cannot be hurt while it charges");
            guard = 0;
            while (boss.evolveUntil != 0 && guard++ < 200) {
                e.step();
                e.buildFrame();
            }
            assertEquals(form + 1, boss.formIdx);
            assertEquals(boss.hpMax, boss.hp, "all its HP back");
            assertEquals(forms.get(form + 1).name(), boss.name);
            assertEquals(form + 1, e.bossesDefeated(), "the form that fell was paid like a boss");
            assertEquals(form == 5 ? "FINAL" : "NORMAL", boss.role);
        }
        // the seventh form dies for real and the dungeon is won
        boss.stunUntil = e.nowMs() + 600_000;
        boss.hp = 1;
        int guard = 0;
        while (!e.finished() && guard++ < 400) {
            play(e);
            e.step();
            e.buildFrame();
        }
        assertTrue(e.finished(), "the last form falls");
        assertEquals(7, e.bossesDefeated());
        assertEquals(7, e.outcome().kills().stream().filter(k -> k.realBoss()).count(), "seven bosses paid");
        assertEquals(com.battleball.proto.MatchResult.WON, e.outcome().result());
    }

    @Test
    void theApeBossUsesTheXaydaMovesWithTheShortenedCooldownsAndNeverTheSelfDestruct() {
        List<StaticData.Boss> forms = new ArrayList<>();
        for (int id = 78; id <= 84; id++) {
            forms.add(d.bosses().get(id));
        }
        MatchSetup setup = new MatchSetup(1, d, d.dungeons().get(3), List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 1, 400), member(2, "NAMEC", 100, false, NAMEC_SKILLS, 1, 400)),
                List.of(new MatchSetup.Encounter(forms.get(0), "NORMAL", null, forms)), List.of(), 10_000_000, new Random(5));
        MatchEngine e = new MatchEngine(setup);
        for (int i = 0; i < 80; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.hpMax = boss.hp = 1_000_000_000_000L;                                  // it never evolves or dies in this test
        boss.hp = (long) (boss.hpMax * 0.6);                                        // hurt enough to regenerate
        java.util.Map<Integer, Integer> casts = new java.util.HashMap<>();
        boolean selfDestructSeen = false;
        for (int i = 0; i < 20 * 70; i++) {                                         // 70 seconds
            e.step();
            var frame = e.buildFrame();
            for (var ef : frame.getEffectsList()) {
                if (ef.getSourceId() == boss.id && (ef.getEffect() == com.battleball.proto.Effect.CAST_START || ef.getEffect() == com.battleball.proto.Effect.TELEGRAPH_RECT)) {
                    casts.merge(ef.getSkillId(), 1, Integer::sum);
                }
            }
            for (var dmg : frame.getDamagesList()) {
                selfDestructSeen |= dmg.getSourceId() == boss.id && dmg.getSkillId() == 14;
            }
            boss.hp = Math.max(boss.hp, (long) (boss.hpMax * 0.5));
        }
        assertFalse(selfDestructSeen, "no boss uses the Tu phat no");
        assertFalse(casts.containsKey(14), "the boss never charges the Tu phat no in a fight (only to evolve, and not in this test)");
        assertTrue(casts.getOrDefault(25, 0) >= 3, "Cadic (cooldown 110 s / 10 = 11 s) is used often: " + casts);
        assertTrue(casts.getOrDefault(25, 0) <= 70 / 11 + 2, "...but not faster than its shortened cooldown: " + casts);
        assertTrue(casts.getOrDefault(21, 0) >= 1 && casts.getOrDefault(21, 0) <= 70 / 18 + 1, "Huyt sao (180 s / 10 = 18 s): " + casts);
        assertTrue(casts.getOrDefault(23, 0) >= 2 && casts.getOrDefault(23, 0) <= 70 / 4 + 2, "Troi (45 s / 10 = 4.5 s): " + casts);
        assertTrue(casts.getOrDefault(8, 0) >= 1 && casts.getOrDefault(8, 0) <= 70 / 2 + 2, "Tai tao nang luong (25 s / 10 = 2.5 s): " + casts);
    }

    /** Uses a control skill of a character on the boss of a dungeon and returns how long the boss is held by it now, in ms: {stun, sleep, chocolate, hold}. */
    private static long[] controlOnBoss(String race, int skillId, int slot) {
        MatchEngine e = engineBare(12, 1, List.of(member(1, race, 100, true, Map.of(0, 7, skillId, 7), 1, 400)), 17);
        for (int i = 0; i < 80; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        Entities.Fighter me = e.controlledFighter();
        boss.x = me.x + 60;
        boss.y = me.y;
        e.submit(() -> e.useSkill(1, slot, boss.id, 1, 0));
        for (int i = 0; i < 3; i++) {
            e.step();
            e.buildFrame();
        }
        long now = e.nowMs();
        return new long[]{Math.max(0, boss.stunUntil - now), Math.max(0, boss.sleepUntil - now), Math.max(0, boss.chocolateUntil - now), Math.max(0, boss.holdUntil - now)};
    }

    @Test
    void theControlSkillsControlABossForAShortTime() {
        long[] stun = controlOnBoss("EARTH", 6, 2);                                          // Thai duong ha san: 9 s on others
        assertTrue(stun[0] > 1500 && stun[0] <= 3000, "the stun holds a boss up to 3 s: " + stun[0]);
        long[] sleep = controlOnBoss("EARTH", 22, 6);                                        // Thoi mien
        assertTrue(sleep[1] > 0 && sleep[1] <= 3000, "the sleep holds a boss: " + sleep[1]);
        long[] sweet = controlOnBoss("NAMEC", 18, 6);                                        // Bien Socola
        assertTrue(sweet[2] > 0 && sweet[2] <= 3000, "the chocolate holds a boss: " + sweet[2]);
        long[] rope = controlOnBoss("XAYDA", 23, 6);                                         // Troi
        assertTrue(rope[3] > 0 && rope[3] <= 3000, "the rope holds a boss: " + rope[3]);
    }

    @Test
    void aStunnedBossLosesTheBlowItWasPreparing() {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "EARTH", 100, true, Map.of(0, 7, 6, 7), 1, 4000)), 21);
        e.forcePlanet("EARTH");
        for (int i = 0; i < 80; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        Entities.Fighter me = e.controlledFighter();
        int zone = 0;
        for (int i = 0; i < 20 * 60 && zone == 0; i++) {
            e.step();
            for (var ef : e.buildFrame().getEffectsList()) {
                if (ef.getSourceId() == boss.id && ef.getEffect() == com.battleball.proto.Effect.TELEGRAPH_RECT) {
                    zone = ef.getSkillId();
                }
            }
        }
        assertTrue(zone != 0, "the boss prepares its ultimate");
        boss.x = me.x + 60;
        boss.y = me.y;
        e.submit(() -> e.useSkill(1, 2, boss.id, 1, 0));                                     // the stun of Thai duong ha san
        boolean dropped = false;
        boolean landed = false;
        for (int i = 0; i < 20 * 4; i++) {                                                   // the zone would have landed after 2 s
            e.step();
            var frame = e.buildFrame();
            for (var ef : frame.getEffectsList()) {
                dropped |= ef.getEffect() == com.battleball.proto.Effect.CAST_END && ef.getSourceId() == boss.id && ef.getTargetId() == boss.id && ef.getSkillId() == 0;
            }
            for (var dmg : frame.getDamagesList()) {
                landed |= dmg.getSourceId() == boss.id && dmg.getSkillId() == zone && dmg.getAmount() > 0;
            }
        }
        assertTrue(dropped, "the client is told to drop the red zone");
        assertFalse(landed, "the blow never lands");
    }

    @Test
    void theAiUsesControlSkillsOnABossAndNeverOnSmallMonsters() {
        MatchEngine e = engine(12, 1, List.of(member(1, "EARTH", 100, true, EARTH_SKILLS, 8, 400), member(2, "XAYDA", 100, false, Map.of(4, 7, 23, 7), 8, 400),
                member(3, "EARTH", 100, false, Map.of(0, 7, 6, 7, 22, 7), 8, 400)), 31);
        java.util.Set<com.battleball.proto.Effect> control = java.util.Set.of(com.battleball.proto.Effect.STUN, com.battleball.proto.Effect.HOLD, com.battleball.proto.Effect.SLEEP,
                com.battleball.proto.Effect.CHOCOLATE, com.battleball.proto.Effect.TELEPORT);
        boolean bossSeen = false;
        int onBoss = 0;
        for (int i = 0; i < 20 * 400; i++) {
            play(e);
            e.step();
            var frame = e.buildFrame();
            java.util.Map<Integer, Entities.Monster> monsters = new java.util.HashMap<>();
            for (var x : e.entities()) {
                if (x instanceof Entities.Monster m) {
                    monsters.put(m.id, m);
                }
            }
            for (var ef : frame.getEffectsList()) {
                if (!control.contains(ef.getEffect()) || ef.getSourceId() == e.controlledFighter().id) {
                    continue;
                }
                Entities.Monster target = monsters.get(ef.getTargetId());
                if (target == null) {
                    continue;
                }
                assertTrue(ef.getSkillId() != 6 && ef.getSkillId() != 26, "the AI never uses a control skill of an area (skill " + ef.getSkillId() + ")");
                assertEquals(Entities.MonsterKind.BOSS, target.kind, "the AI aimed " + ef.getEffect() + " (skill " + ef.getSkillId() + ") at a small monster");
                onBoss++;
                bossSeen |= target.kind == Entities.MonsterKind.BOSS;
            }
            if (e.finished()) {
                break;
            }
        }
        assertTrue(bossSeen && onBoss > 0, "the AI controls the boss when it is there");
    }

    /** The Namek heal (skill 7) on a dead ally standing {@code gap} px away; the target the client sends is the boss it has in focus (or none). Returns whether the ally is alive after it. */
    private static boolean healRevives(double gap, boolean bossTarget) {
        MatchEngine e = engineBare(12, 1, List.of(member(1, "NAMEC", 100, true, NAMEC_SKILLS, 1, 400), member(2, "EARTH", 100, false, EARTH_SKILLS, 1, 400)), 41);
        for (int i = 0; i < 80; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter me = e.controlledFighter();
        Entities.Fighter ally = e.squad().stream().filter(x -> x != me).findFirst().orElseThrow();
        ally.alive = false;
        ally.hp = 0;
        ally.x = me.x + gap;
        ally.y = me.y;
        Entities.Entity boss = e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        int slot = slotOf(7);
        int target = bossTarget ? boss.id : 0;
        e.submit(() -> e.useSkill(1, slot, target, 1, 0));
        for (int i = 0; i < 5; i++) {
            e.step();
            e.buildFrame();
        }
        return ally.alive;
    }

    @Test
    void theNamekHealRevivesADeadAllyEvenWhenItLiesFarAway() {
        assertTrue(healRevives(60, false), "a dead ally beside the caster, no target");
        assertTrue(healRevives(60, true), "...with the boss in focus (the target the client sends)");
        assertTrue(healRevives(160, true), "...at 160 px");
        assertTrue(healRevives(300, true), "a body lies where it fell: it is revived from up to revive_skill_reach_px (320)");
        assertFalse(healRevives(400, true), "...but not from any distance");
    }

    /** Releases the Qua cau kenh khi of a level-100 character at the given skill level on the boss of dungeon {@code dungeon}; returns {damage dealt to the boss, the attack of the caster}. */
    private static double[] spiritBomb(int dungeon, int skillLevel) {
        MatchEngine e = engineBare(dungeon, 1, List.of(member(1, "EARTH", 100, true, Map.of(0, 7, 10, skillLevel), 1, 1000)), 41);
        for (int i = 0; i < 80; i++) {
            e.step();
            e.buildFrame();
        }
        Entities.Fighter me = e.controlledFighter();
        me.critRatePct = 0;
        Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
        boss.x = me.x + 100;
        boss.y = me.y;
        boss.armorReduction = 0;
        double hp0 = boss.hp;
        long dealt = 0;
        e.submit(() -> e.useSkill(1, slotOf(10), boss.id, 1, 0));
        for (int i = 0; i < 20 * 8; i++) {
            e.step();
            for (var dm : e.buildFrame().getDamagesList()) {
                if (dm.getSkillId() == 10 && dm.getTargetId() == boss.id) {
                    dealt += dm.getAmount();
                }
            }
        }
        return new double[]{dealt, me.atk, hp0};
    }

    @Test
    void theSpiritBombGrowsWithItsLevelAndDoesNotTakeATenthOfTheLifeOfABoss() {
        double[] lv7 = spiritBomb(3, 7);
        double[] lv1 = spiritBomb(3, 1);
        assertTrue(lv7[0] > 0 && lv1[0] > 0, "the bomb lands");
        assertTrue(lv7[0] > lv1[0] * 1.2, "a higher level hits harder: " + lv1[0] + " then " + lv7[0]);
        double cap = (11 + 6) * lv7[1] * 1.06;                                         // level 7: 11 x attack plus at most 6 x attack of the HP part, 5% spread
        assertTrue(lv7[0] <= cap, "the bomb stays under " + cap + " (it was " + lv7[0] + "; the boss has " + lv7[2] + " HP, a tenth of it is " + lv7[2] / 10 + ")");
        assertTrue(lv7[0] < lv7[2] / 10, "it is far from a tenth of the HP of the boss");
    }

    @Test
    void aDifficultyScalesTheMonstersAndTheRewardIsRead() {
        var easy = com.battleball.server.game.match.Difficulty.find(d, "");
        assertEquals("EASY", easy.key(), "no key = the default, the easiest");
        var hell = com.battleball.server.game.match.Difficulty.find(d, "hell");
        assertEquals("HELL", hell.key());
        assertNull(com.battleball.server.game.match.Difficulty.find(d, "NIGHTMARE"));
        var all = com.battleball.server.game.match.Difficulty.all(d);
        assertEquals(List.of("EASY", "HARD", "HELL"), all.stream().map(com.battleball.server.game.match.Difficulty::key).toList());
        for (int i = 1; i < all.size(); i++) {
            assertTrue(all.get(i).atkMult() > all.get(i - 1).atkMult() && all.get(i).hpMult() >= all.get(i - 1).hpMult(), "harder means stronger monsters");
            assertTrue(all.get(i).rewardMult() > all.get(i - 1).rewardMult() && all.get(i).dropMult() >= all.get(i - 1).dropMult(), "...and a bigger reward");
        }
        // the same dungeon and seed at three difficulties: the boss follows the multipliers
        double[] hp = new double[3];
        double[] atk = new double[3];
        com.battleball.server.game.match.Difficulty[] levels = {com.battleball.server.game.match.Difficulty.NONE, easy, hell};
        for (int i = 0; i < 3; i++) {
            MatchSetup setup = new MatchSetup(1, d, d.dungeons().get(12), List.of(member(1, "EARTH", 100, true, EARTH_SKILLS)), encounters(12, 1), List.of(), 10_000_000, new Random(9))
                    .withDifficulty(levels[i]);
            MatchEngine e = new MatchEngine(setup);
            for (int k = 0; k < 80; k++) {
                e.step();
                e.buildFrame();
            }
            Entities.Monster boss = (Entities.Monster) e.entities().stream().filter(x -> x instanceof Entities.Monster).findFirst().orElseThrow();
            hp[i] = boss.hpMax;
            atk[i] = boss.atkPerHit;
        }
        assertEquals(easy.hpMult(), hp[1] / hp[0], 0.001);
        assertEquals(easy.atkMult(), atk[1] / atk[0], 0.001);
        assertEquals(hell.hpMult(), hp[2] / hp[0], 0.001);
        assertEquals(hell.atkMult(), atk[2] / atk[0], 0.001);
    }
}
