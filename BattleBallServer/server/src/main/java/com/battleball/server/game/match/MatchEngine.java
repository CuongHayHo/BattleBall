package com.battleball.server.game.match;

import com.battleball.proto.DamageEvent;
import com.battleball.proto.Effect;
import com.battleball.proto.EffectEvent;
import com.battleball.proto.EntityKind;
import com.battleball.proto.EntitySpawn;
import com.battleball.proto.EntityState;
import com.battleball.proto.MatchFrame;
import com.battleball.proto.MatchResult;
import com.battleball.proto.ProjectileState;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.game.StatCalculator;
import com.battleball.server.game.match.Entities.Cast;
import com.battleball.server.game.match.Entities.Channel;
import com.battleball.server.game.match.Entities.Delayed;
import com.battleball.server.game.match.Entities.Dummy;
import com.battleball.server.game.match.Entities.Entity;
import com.battleball.server.game.match.Entities.Fighter;
import com.battleball.server.game.match.Entities.Monster;
import com.battleball.server.game.match.Entities.MonsterKind;
import com.battleball.server.game.match.Entities.PetEntity;
import com.battleball.server.game.match.Entities.Projectile;
import com.battleball.server.game.match.Entities.Team;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.random.RandomGenerator;

/**
 * The authoritative simulation of one match at 20 Hz. One thread steps it; the network thread only queues inputs ({@link #submit}).
 * Rules: docs/gameplay/game-design-doc.md 7.4 (damage and armor) and 7.5 (boss behaviour), docs/database/BattleBall.md 4.1 and 4.2, docs/protocol/BattleBall.md 6.
 * The client reports its own position (CONFIRMED: not validated, only kept inside the arena); everything else is decided here.
 * Training (setup.training() != null, the starter village): no bosses, no end by time or defeat, dummies that refill their HP, the AI characters stand where
 * the client's village puts them (they still choose and use their skills here), a character that dies stands up again.
 */
public final class MatchEngine {
    public static final int TICK_MS = 50;

    private final MatchSetup setup;
    private final StaticData d;
    private final Map<String, Object> rules;
    private final RandomGenerator rng;
    private final ConcurrentLinkedQueue<Runnable> inputs = new ConcurrentLinkedQueue<>();

    private long now;
    private int tick;
    private int nextId = 1;
    private int nextProjectileId = 1;
    private final List<Entity> entities = new ArrayList<>();
    private final List<Projectile> projectiles = new ArrayList<>();
    private final List<Delayed> delayed = new ArrayList<>();
    private final List<TelegraphArea> telegraphs = new ArrayList<>();
    private Fighter controlled;
    private final List<Fighter> squad = new ArrayList<>();
    private final MatchSetup.Training training;
    private final double arenaW;
    private final double arenaH;
    private long lastInputAt;            // training: the session ends after training.idleEndMs without input
    private final boolean dungeon;       // a real match: a flying arena, waves of small monsters before every boss (docs/architecture/BattleBallServer.md 15.25)
    // the part of the arena the player's screen shows (reported by the client, top-left corner and size): the AI fight only what is inside it
    private double viewX;
    private double viewY;
    private double viewW;
    private double viewH;
    private boolean viewReported;
    // the stage in progress: the waves come first, the boss after the last of them
    private boolean stageStarted;
    private int waveIdx;
    private int waveSize;
    private int pendingSpawns;
    private long firstWaveAt;
    private long lastWaveAt;
    private long bossReadyAt;
    private int summonedThisBoss;
    private long nextSummonAt;
    private static final String[] PLANETS = {"EARTH", "NAMEC", "XAYDA"};
    private String forcedPlanet;         // tests: every boss takes this planet

    // frame buffers (drained by buildFrame)
    private final List<DamageEvent> damageBuf = new ArrayList<>();
    private final List<EffectEvent> effectBuf = new ArrayList<>();
    private final List<EntitySpawn> spawnBuf = new ArrayList<>();
    private final List<Integer> despawnBuf = new ArrayList<>();
    private int lastSeq;

    // encounters
    private int encounterIdx;
    private long nextBossAt;
    private Monster currentBoss;
    private long bossSpawnedAt;
    private int bossesDefeated;
    private int minionsSpawned;
    private int minionPoolCursor;
    private long nextWaveAt;
    private boolean finalDefeated;
    private double fastestBossKillS = Double.MAX_VALUE;

    // pause, revive, end
    private boolean paused;
    private boolean finished;
    private MatchResult result = MatchResult.MATCH_RESULT_UNSPECIFIED;
    private int revivesUsed;
    private long reviveGoldSpent;

    // rewards in progress
    private long goldEarned;
    private final Map<Long, Double> expRaw = new HashMap<>();
    private final List<MatchOutcome.Kill> kills = new ArrayList<>();
    private int streak;
    private long lastKillAt = Long.MIN_VALUE / 2;

    /** A target inside the jar of Ma phong ba: it takes damage every {@code tickMs} until {@code until} (Nro EffectSkin.updateMaPhongBa, MobEffectSkill). */
    private static final class Jar {
        final Entity victim;
        final Entity owner;
        final long damage;
        long nextAt;
        final long until;
        final long tickMs;

        Jar(Entity victim, Entity owner, long damage, long firstAt, long until, long tickMs) {
            this.victim = victim;
            this.owner = owner;
            this.damage = damage;
            this.nextAt = firstAt;
            this.until = until;
            this.tickMs = tickMs;
        }
    }

    private final List<Jar> jars = new ArrayList<>();
    /** How far a fighter may drift between two position reports before it counts as moving (Nro px; reports of a standing client differ by rounding only). */
    private static final double MOVE_EPS = 2.0;

    /** A visible danger zone (boss ultimate): allies try to step out of it. */
    private record TelegraphArea(long ownerId, boolean rect, double x, double y, double dirX, double dirY, double length, double width, long until) {
    }

    public MatchEngine(MatchSetup setup) {
        this.setup = setup;
        this.d = setup.data();
        this.rules = d.balanceObj("match_rules");
        this.rng = setup.random();
        this.training = setup.training();
        this.dungeon = training == null;
        this.arenaW = training != null ? training.arenaW() : rule("dg_arena_w", rule("arena_w", 1600));
        this.arenaH = training != null ? training.arenaH() : rule("dg_arena_h", rule("arena_h", 900));
        double w = arenaW;
        double h = arenaH;
        Fighter ctrl = null;
        int i = 0;
        // training: the squad starts left of the first dummy (the client reports the real positions on its first frame)
        MatchSetup.Dummy first = training == null || training.dummies().isEmpty() ? null : training.dummies().get(0);
        for (MatchSetup.SquadMember m : setup.squad()) {
            Fighter f = first != null ? spawnFighter(m, clamp(first.x() - 200 - i * 80, 0, w), first.y())
                    : spawnFighter(m, 280, h / 2 + (i == 0 ? 0 : (i == 1 ? -110 : 110)));
            squad.add(f);
            if (m.controlled()) {
                ctrl = f;
            }
            expRaw.put(m.characterId(), 0.0);
            if (m.partnerCharacterId() != 0) {
                expRaw.put(m.partnerCharacterId(), 0.0);
            }
            i++;
        }
        this.controlled = ctrl;
        this.nextBossAt = (long) (rule("boss_spawn_delay_s", 3) * 1000);
        if (training != null) {
            for (MatchSetup.Dummy dm : training.dummies()) {
                spawnDummy(dm);
            }
        }
        if (w <= 0) {
            throw new IllegalStateException("arena");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- public API

    /** Queue an input from the network thread; it runs at the start of the next tick. */
    public void submit(Runnable r) {
        inputs.add(r);
    }

    public boolean finished() {
        return finished;
    }

    public void setPaused(boolean p) {
        this.paused = p;
    }

    public boolean paused() {
        return paused;
    }

    public long nowMs() {
        return now;
    }

    public Fighter controlledFighter() {
        return controlled;
    }

    /**
     * The player takes over another fighter of the squad (owner request 2026-10-11): the one controlled until now becomes AI where it stands, nothing of either is reset (HP, Ki,
     * buffs, the ape form, a skill in progress, cooldowns). A dead fighter can be taken too (the revive then applies to it). Returns the new controlled fighter, or null.
     */
    public Fighter switchControl(int entityId) {
        lastInputAt = now;
        if (finished) {
            return null;
        }
        for (Fighter f : squad) {
            if (f.id == entityId) {
                if (f != controlled) {
                    if (controlled != null) {
                        controlled.controlled = false;
                    }
                    f.controlled = true;
                    controlled = f;
                }
                return f;
            }
        }
        return null;
    }

    public int bossesDefeated() {
        return bossesDefeated;
    }

    public int bossCount() {
        return setup.encounters().size();
    }

    public int revivesUsed() {
        return revivesUsed;
    }

    public int revivesLeft() {
        return (int) rule("revives_per_match", 2) - revivesUsed;
    }

    public List<Fighter> squad() {
        return squad;
    }

    /** Cooldowns still running, as milliseconds left: character id, then skill id (owner request 2026-10-09: they are kept when the player leaves). */
    public java.util.Map<Long, java.util.Map<Integer, Long>> cooldownsLeft() {
        java.util.Map<Long, java.util.Map<Integer, Long>> out = new java.util.HashMap<>();
        for (Fighter f : squad) {
            for (java.util.Map.Entry<Integer, Long> e : f.cooldownUntil.entrySet()) {
                StaticData.Skill sk = f.skillBySlot.get(e.getKey());
                long left = e.getValue() - now;
                if (sk != null && left > 0) {
                    out.computeIfAbsent(f.characterId, k -> new java.util.HashMap<>()).put(sk.id(), left);
                }
            }
        }
        return out;
    }

    /** Cooldowns kept from an earlier session: character id, then skill id, then the Unix time (ms) the skill is ready; {@code wallNow} is the Unix time now. */
    public void applyCooldowns(java.util.Map<Long, java.util.Map<Integer, Long>> readyAt, long wallNow) {
        for (Fighter f : squad) {
            java.util.Map<Integer, Long> m = readyAt.get(f.characterId);
            if (m == null) {
                continue;
            }
            f.skillBySlot.forEach((slot, sk) -> {
                Long r = m.get(sk.id());
                if (r != null && r > wallNow) {
                    f.cooldownUntil.put(slot, now + (r - wallNow));
                }
            });
        }
    }

    /** What is left of the cooldown of a slot of this fighter now (ms). */
    public long cooldownLeftMs(Fighter f, int slot) {
        return Math.max(0, f.cooldownUntil.getOrDefault(slot, 0L) - now);
    }

    public List<Entity> entities() {
        return entities;
    }

    public boolean training() {
        return training != null;
    }

    public double arenaWidth() {
        return arenaW;
    }

    public double arenaHeight() {
        return arenaH;
    }

    /** Tests: every boss that comes takes this planet instead of a random one. */
    public void forcePlanet(String planet) {
        this.forcedPlanet = planet;
    }

    /**
     * The client says which part of the arena its screen shows (top-left corner and size, the pixels of the arena). The size is kept inside what a screen can be, the corner
     * inside the arena; the AI characters fight only what lies in this rectangle.
     */
    public void reportView(double x, double y, double w, double h) {
        if (w <= 0 || h <= 0) {
            return;
        }
        viewW = clamp(w, rule("dg_view_min_w", 240), rule("dg_view_max_w", 1400));
        viewH = clamp(h, rule("dg_view_min_h", 160), rule("dg_view_max_h", 900));
        viewX = clamp(x, 0, Math.max(0, arenaW - viewW));
        viewY = clamp(y, 0, Math.max(0, arenaH - viewH));
        viewReported = true;
    }

    /** The part of the arena the screen shows, as x0, y0, x1, y1: what the client reported, or a default one centred on the controlled character. */
    double[] view() {
        double w = viewReported ? viewW : rule("dg_view_w", 640);
        double h = viewReported ? viewH : rule("dg_view_h", 360);
        double x0 = viewReported ? viewX : (controlled == null ? 0 : controlled.x - w / 2);
        double y0 = viewReported ? viewY : (controlled == null ? 0 : controlled.y - h / 2);
        x0 = clamp(x0, 0, Math.max(0, arenaW - w));
        y0 = clamp(y0, 0, Math.max(0, arenaH - h));
        return new double[] {x0, y0, x0 + w, y0 + h};
    }

    private static boolean inside(double[] v, Entity e) {
        return e.x + e.radius >= v[0] && e.x - e.radius <= v[2] && e.y + e.radius >= v[1] && e.y - e.radius <= v[3];
    }

    /** How far a ray from (x, y) along (dx, dy) goes before it leaves the screen; 0 when the start is not on the screen. */
    private double distanceToViewEdge(double x, double y, double dx, double dy) {
        double[] v = view();
        if (x < v[0] || x > v[2] || y < v[1] || y > v[3]) {
            return 0;
        }
        double t = Double.MAX_VALUE;
        if (dx > 1e-9) {
            t = Math.min(t, (v[2] - x) / dx);
        } else if (dx < -1e-9) {
            t = Math.min(t, (v[0] - x) / dx);
        }
        if (dy > 1e-9) {
            t = Math.min(t, (v[3] - y) / dy);
        } else if (dy < -1e-9) {
            t = Math.min(t, (v[1] - y) / dy);
        }
        return t == Double.MAX_VALUE ? 0 : Math.min(t, 4000);
    }

    /** Training: an AI character of the squad stands where the client's village puts it (kept inside the arena). */
    public void reportAlly(int entityId, double x, double y, double dx, double dy) {
        lastInputAt = now;
        if (training == null) {
            return;
        }
        for (Fighter f : squad) {
            if (f.id == entityId && !f.controlled && f.alive && f.canMove(now) && f.cast == null && f.channel == null) {
                moveFighter(f, clamp(x, 0, arenaW), clamp(y, 0, arenaH));
                normalizeInto(f, dx, dy);
            }
        }
    }

    /** A fighter moves: the skills that need it to stand still end (Nro PlayerService.playerMove: stopCharge, removeUseTroi). */
    private void moveFighter(Fighter f, double nx, double ny) {
        if (Math.hypot(nx - f.x, ny - f.y) > MOVE_EPS) {
            if (now < f.regenUntil && Math.hypot(nx - f.regenAtX, ny - f.regenAtY) > MOVE_EPS) {
                endRegen(f);
            }
            releaseHolds(f);
        }
        f.x = nx;
        f.y = ny;
    }

    /** The controlled player moved: the position is taken as reported but kept inside the arena. */
    public void reportPosition(int seq, double x, double y, double dx, double dy) {
        lastSeq = Math.max(lastSeq, seq);
        lastInputAt = now;
        Fighter f = controlled;
        if (f == null || !f.alive || !f.canMove(now) || f.cast != null || f.channel != null) {
            return;
        }
        moveFighter(f, clamp(x, 0, arenaW), clamp(y, 0, arenaH));
        normalizeInto(f, dx, dy);
    }

    /** The controlled player asks to use the skill in a slot. Returns true when the skill started. */
    public boolean useSkill(int seq, int slot, int targetId, double dx, double dy) {
        lastSeq = Math.max(lastSeq, seq);
        lastInputAt = now;
        Fighter f = controlled;
        if (f == null || finished) {
            return false;
        }
        if (dx != 0 || dy != 0) {
            normalizeInto(f, dx, dy);
        }
        return tryUseSkill(f, slot, targetId, f.dirX, f.dirY);
    }

    /** Revive the controlled character (max 2 per match, paid in gold when the match is settled). */
    public boolean revive() {
        Fighter f = controlled;
        if (f == null || f.alive || finished || revivesLeft() <= 0) {
            return false;
        }
        Map<String, Object> eco = d.balanceObj("item_economy_wins");
        double wins = revivesUsed == 0 ? Json.num(eco, "revive_1", 0.5) : Json.num(eco, "revive_2", 1.0);
        long cost = Math.round(wins * d.balanceNum("gold_per_win_per_level", 1000) * Math.max(1, f.level));
        if (setup.goldAvailable() - reviveGoldSpent < cost) {
            return false;
        }
        revivesUsed++;
        reviveGoldSpent += cost;
        f.alive = true;
        f.hp = Math.round(f.hpMax * rule("revive_hp_pct", 100) / 100.0);
        f.ki = f.kiMax;
        f.cast = null;
        f.stunUntil = f.holdUntil = f.sleepUntil = f.chocolateUntil = f.jarUntil = 0;
        f.channel = null;
        f.shieldUntil = now + 3000;
        addEffect(Effect.SHIELD, f, f, 3000, 0);
        return true;
    }

    /** The player gives up. */
    public void leave() {
        end(MatchResult.ABANDONED);
    }

    /** Time ran out on the server (too long a match). */
    public void expire() {
        end(MatchResult.EXPIRED);
    }

    public MatchOutcome outcome() {
        Map<Long, Long> dmg = new HashMap<>();
        for (Fighter f : squad) {
            dmg.put(f.characterId, f.damageDealt);
        }
        return new MatchOutcome(result, bossesDefeated, bossCount(), (int) (now / 1000), goldEarned, Map.copyOf(expRaw), List.copyOf(kills),
                revivesUsed, reviveGoldSpent, dmg, finalDefeated, fastestBossKillS == Double.MAX_VALUE ? 0 : fastestBossKillS);
    }

    // ---------------------------------------------------------------------------------------------------------------- the tick

    public void step() {
        Runnable r;
        while ((r = inputs.poll()) != null) {
            r.run();
        }
        if (finished || paused) {
            return;
        }
        now += TICK_MS;
        tick++;
        runDelayed();
        double dt = TICK_MS / 1000.0;
        for (Fighter f : squad) {
            if (f.alive) {
                updateFighter(f, dt);
            }
        }
        for (Entity e : new ArrayList<>(entities)) {
            if (e instanceof PetEntity p && p.alive) {
                updatePet(p);
            }
        }
        if (training == null) {
            updateEncounters(dt);
        }
        for (Entity e : new ArrayList<>(entities)) {
            if (e instanceof Monster m && m.alive) {
                updateMonster(m, dt);
            }
        }
        updateProjectiles(dt);
        updateJars();
        telegraphs.removeIf(t -> t.until() <= now);
        if (training != null) {
            if (now - lastInputAt > training.idleEndMs()) {
                end(MatchResult.ABANDONED);                 // nobody at the keyboard: the session closes
            }
            return;                                         // training never ends by defeat or time
        }
        if (!finished && squad.stream().noneMatch(f -> f.alive)) {
            end(MatchResult.LOST);
        }
        if (!finished && now >= rule("match_max_s", 1800) * 1000) {
            end(MatchResult.EXPIRED);
        }
    }

    private void end(MatchResult r) {
        if (!finished) {
            finished = true;
            result = r;
        }
    }

    private void runDelayed() {
        for (Iterator<Delayed> it = delayed.iterator(); it.hasNext(); ) {
            Delayed x = it.next();
            if (x.at <= now) {
                it.remove();
                if (x.owner == null || x.owner.alive) {
                    x.action.run();
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- fighters

    private Fighter spawnFighter(MatchSetup.SquadMember m, double x, double y) {
        StatCalculator.CharacterStats s = m.stats();
        Fighter f = new Fighter(nextId++, m.name(), m.characterId(), m.race(), m.level(), m.controlled(), m.fusedMain(), m.partnerCharacterId(), m.costumeId(),
                rule("player_radius", 20), s.hpRegenPctPerSec(), s.kiRegenPctPerSec());
        f.x = x;
        f.y = y;
        f.hpMax = s.hpMax();
        f.hp = s.hpMax();
        f.kiMax = s.kiMax();
        f.ki = s.kiMax();
        f.atk = s.atk();
        f.armorStat = s.armor();
        f.armorReduction = StatCalculator.armorReduction(d, s.armor());
        f.critRatePct = s.critRatePct();
        f.critDmgPct = s.critDmgPct();
        f.dodgePct = s.dodgePct();
        f.lifestealPct = s.other("LIFESTEAL_PCT");
        f.kiStealPct = s.other("KI_STEAL_PCT");
        f.base = s;
        f.pet = m.pet();
        f.nextPetAt = m.pet() == null ? Long.MAX_VALUE : (long) m.pet().intervalS() * 1000L;
        for (Map.Entry<Integer, Integer> e : m.skills().entrySet()) {
            StaticData.Skill sk = d.skills().get(e.getKey());
            if (sk != null && sk.enabled() && e.getValue() > 0) {
                f.skillBySlot.put(sk.slot(), sk);
                f.skillLevelBySlot.put(sk.slot(), e.getValue());
            }
        }
        entities.add(f);
        spawnBuf.add(spawnProto(f));
        return f;
    }

    /**
     * New figures for fighters that are already in the fight (gear worn, taken off or upgraded in the village during a training session; owner request 2026-10-10).
     * Engine thread. Whatever the fight added on top stays (the monkey, Huýt sáo, the pet buff): only the difference between the old and the new figures is applied.
     * The current HP and Ki stay as they are, cut to the new maximum.
     */
    public void applyStats(Map<Long, StatCalculator.CharacterStats> byCharacter) {
        for (Entity e : entities) {
            if (!(e instanceof Fighter f) || f.base == null) {
                continue;
            }
            StatCalculator.CharacterStats n = byCharacter.get(f.characterId);
            if (n == null) {
                continue;
            }
            StatCalculator.CharacterStats o = f.base;
            f.hpMax = Math.max(1, f.hpMax + (n.hpMax() - o.hpMax()));
            f.hp = Math.min(f.hp, f.hpMax);
            f.kiMax = Math.max(1, f.kiMax + (n.kiMax() - o.kiMax()));
            f.ki = Math.min(f.ki, f.kiMax);
            f.atk += n.atk() - o.atk();
            f.armorStat = n.armor();
            f.armorReduction = StatCalculator.armorReduction(d, n.armor());
            if (f.monkeyUntil > 0) {
                f.monkeyCritBefore = n.critRatePct();              // the monkey keeps its own rate; the new one comes back with the form
            } else {
                f.critRatePct = n.critRatePct();
            }
            f.critDmgPct = n.critDmgPct();
            f.dodgePct = n.dodgePct();
            f.lifestealPct = n.other("LIFESTEAL_PCT");
            f.kiStealPct = n.other("KI_STEAL_PCT");
            f.hpRegenPctPerSec = n.hpRegenPctPerSec();
            f.kiRegenPctPerSec = n.kiRegenPctPerSec();
            f.base = n;
        }
    }

    private void updateFighter(Fighter f, double dt) {
        // regeneration: Ki every second, HP only while Ki is above zero (CONFIRMED rule)
        f.ki = Math.min(f.kiMax, f.ki + f.kiMax * f.kiRegenPctPerSec / 100.0 * dt);
        if (f.ki > 0) {
            heal(f, f.hpMax * f.hpRegenPctPerSec / 100.0 * dt);
        }
        if (now < f.regenUntil) {
            // Tái tạo năng lượng heals the HP and the Ki (NPoint.update); it ends when both are full, or when the fighter cannot act (EffectSkill.isHaveEffectSkill)
            if (!f.canAct(now) || (f.hp >= f.hpMax && f.ki >= f.kiMax)) {
                endRegen(f);
            } else {
                heal(f, f.hpMax * f.regenPctPerSec / 100.0 * dt);
                f.ki = Math.min(f.kiMax, f.ki + f.kiMax * f.regenPctPerSec / 100.0 * dt);
            }
        }
        if (f.monkeyUntil > 0 && now >= f.monkeyUntil) {
            endMonkey(f);
        }
        if (f.hpBonusAmount > 0 && now >= f.hpBonusUntil) {
            f.hpMax -= f.hpBonusAmount;
            f.hp = Math.min(f.hp, f.hpMax);
            f.hpBonusAmount = 0;
        }
        if (f.pet != null && now >= f.nextPetAt) {
            f.nextPetAt = now + f.pet.intervalS() * 1000L;
            f.buffUntil = now + f.pet.durationS() * 1000L;
            if (f.pet.statKey().equals("CRIT_RATE_PCT")) {
                f.buffCritRatePct = f.pet.value();
            } else if (f.pet.statKey().equals("ARMOR_PCT")) {
                f.buffArmorMult = 1 + f.pet.value() / 100.0;
            }
            addEffect(Effect.BUFF, f, f, f.pet.durationS() * 1000, 0);
        }
        if (now >= f.buffUntil) {
            f.buffArmorMult = 1.0;
        }
        if (f.channel != null) {
            Channel ch = f.channel;
            if (!f.canAct(now)) {
                breakChannel(f);                     // stun or sleep closes the skill (Nro updateSkillSpecial: closeSkillSpecial)
            } else {
                while (ch.nextTickAt <= now && ch.nextTickAt < ch.endsAt) {
                    ultimateTick(f, ch);
                    ch.nextTickAt += ch.tickMs;
                }
                if (now >= ch.endsAt) {
                    f.channel = null;                // done: the busy bit goes away with it (CAST_END is only sent when a skill is broken)
                }
            }
            return;
        }
        if (f.cast != null) {
            if (!f.canAct(now)) {
                Cast broken = f.cast;
                f.cast = null;                       // stun or sleep breaks the cast
                addEffect(Effect.CAST_END, f, f, 0, broken.skill.id());
            } else if (now >= f.cast.endsAt) {
                Cast c = f.cast;
                f.cast = null;
                finishCast(f, c);
            }
            return;
        }
        if (!f.controlled) {
            allyAct(f, dt);
        }
    }

    private void heal(Entity e, double amount) {
        if (e.alive && amount > 0) {
            e.hp = Math.min(e.hpMax, e.hp + Math.round(amount + 0.4999));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- skills

    private boolean tryUseSkill(Fighter f, int slot, int targetId, double dx, double dy) {
        if (!f.alive || !f.canAct(now) || f.cast != null || f.channel != null) {
            return false;
        }
        // one skill at a time, in order (Nro: Char.setSkillPaint does nothing while the paint of the last skill plays or its dart is still flying)
        if (now < f.actionUntil || projectiles.stream().anyMatch(p -> p.owner == f)) {
            return false;
        }
        StaticData.Skill sk = f.skillBySlot.get(slot);
        Integer lv = f.skillLevelBySlot.get(slot);
        if (sk == null || lv == null) {
            return false;
        }
        StaticData.SkillLevel L = sk.level(lv);
        if (L == null || now < f.cooldownUntil.getOrDefault(slot, 0L)) {
            return false;
        }
        boolean allKi = "ALL".equals(sk.kiMode());
        double kiCost = allKi ? f.ki : (L.kiCostPct() == null ? 0 : f.kiMax * L.kiCostPct() / 100.0);
        if (allKi ? f.ki <= 0 : f.ki + 1e-9 < kiCost) {
            return false;
        }
        Entity target = targetId == 0 ? null : find(targetId);
        double ndx = dx, ndy = dy;
        if (ndx == 0 && ndy == 0) {
            ndx = f.dirX;
            ndy = f.dirY;
        }
        double len = Math.hypot(ndx, ndy);
        ndx /= len == 0 ? 1 : len;
        ndy /= len == 0 ? 1 : len;
        // using any skill ends the regeneration (stopCharge) and lets go of what the fighter holds (removeUseTroi), before the skill itself (SkillService.useSkill)
        endRegen(f);
        releaseHolds(f);
        boolean started = startSkill(f, sk, L, slot, target, ndx, ndy, kiCost, allKi);
        if (started) {
            f.actionUntil = now + (long) rule("skill_gap_ms", 500);
        }
        return started;
    }

    private boolean startSkill(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L, int slot, Entity target, double dx, double dy, double kiCost, boolean allKi) {
        Map<String, Object> p = L.params();
        int id = sk.id();
        long cd = Math.round(L.cooldownMs() * Math.max(0.0, 1 - f.gen("SKILL_COOLDOWN_PCT", id) / 100.0));       // GEN: a shorter cooldown of this skill
        switch (id) {
            case 0, 2, 4, 9, 17 -> {                                                       // punches, Kaioken, Liên hoàn: instant melee rectangle
                if (id == 9) {
                    long cost = Math.round(f.hpMax * Math.max(0.0, Json.num(p, "selfHpCostPctOfMax", 10) + f.gen("SKILL_HP_COST_DELTA", 9)) / 100.0);
                    if (f.hp <= cost) {
                        return false;
                    }
                    f.hp -= cost;
                }
                aimAt(f, target, dx, dy);
                pay(f, slot, cd, kiCost, allKi);
                meleeRect(f, sk, L);
                return true;
            }
            case 1, 3, 5 -> {                                                              // Kamejoko, Masenko, Antomic: a projectile
                aimAt(f, target, dx, dy);
                pay(f, slot, cd, kiCost, allKi);
                double speed = L.projectileSpeed() == null ? 480 : L.projectileSpeed();
                double range = L.lengthPx() == null ? 300 : L.lengthPx();
                if (dungeon && rule("dg_skill2_to_view_edge", 1) > 0) {
                    range = Math.max(range, distanceToViewEdge(f.x, f.y, f.dirX, f.dirY) + 40);        // in a dungeon the chuong flies on through everything to the edge of the screen (owner request 2026-10-11)
                }
                double radius = L.widthPx() == null ? 40 : L.widthPx() / 2.0;
                projectiles.add(new Projectile(nextProjectileId++, f, f.x, f.y, f.dirX, f.dirY, speed, range, radius, f.atk * pct(L) / 100.0, id, false, false));
                return true;
            }
            case 24, 25 -> {                                                               // Super Kamejoko, Cađíc liên hoàn chưởng: gong then a big rectangle
                aimAt(f, target, dx, dy);
                pay(f, slot, cd, kiCost, allKi);
                beginCast(f, sk, L, (long) Json.num(p, "gongMs", 2000), target, f.dirX, f.dirY);
                addEffect(Effect.CAST_START, f, f, (int) Json.num(p, "gongMs", 2000), id);
                return true;
            }
            case 26 -> {                                                                   // Ma phong ba: gong then trap everything in the half circle
                aimAt(f, target, dx, dy);
                pay(f, slot, cd, kiCost, allKi);
                beginCast(f, sk, L, (long) Json.num(p, "gongMs", 2000), target, f.dirX, f.dirY);
                addEffect(Effect.CAST_START, f, f, (int) Json.num(p, "gongMs", 2000), id);
                return true;
            }
            case 11 -> {                                                                   // Makankosappo: charge, then a long beam from all the Ki
                aimAt(f, target, dx, dy);
                double kiUsed = f.ki;
                pay(f, slot, cd, kiCost, allKi);
                beginCastWithKi(f, sk, L, (long) Json.num(p, "chargeMs", 3000), target, f.dirX, f.dirY, kiUsed);
                addEffect(Effect.CAST_START, f, f, (int) Json.num(p, "chargeMs", 3000), id);
                return true;
            }
            case 10, 14 -> {                                                               // Quả cầu kênh khi, Tự phát nổ: charge then a circle
                pay(f, slot, cd, kiCost, allKi);
                long charge = (long) Json.num(p, "chargeMs", 2000);
                beginCast(f, sk, L, charge, target, f.dirX, f.dirY);
                addEffect(Effect.CAST_START, f, f, (int) charge, id);
                return true;
            }
            case 6 -> {                                                                    // Thái Dương Hạ San: stun everyone around the caster
                pay(f, slot, cd, kiCost, allKi);
                double radius = L.radiusPx() == null ? 150 : L.radiusPx();
                long ms = Math.round(Json.num(p, "stunMs", 3000) * (1 + f.gen("SKILL_DURATION_MULT", 6)));      // GEN: the stun lasts longer
                addEffect(Effect.CAST_START, f, f, 0, id);
                for (Entity e : enemiesOf(f)) {
                    if (f.distance(e) <= radius + e.radius) {
                        stun(e, ms);
                        addEffect(Effect.STUN, f, e, (int) controlMs(e, ms), id);
                    }
                }
                return true;
            }
            case 7 -> {                                                                    // Trị thương
                Fighter ally = healTarget(f, target, L);
                if (ally == null) {
                    return false;
                }
                pay(f, slot, cd, kiCost, allKi);
                double pct = Json.num(p, "healPctOfMaxHpAndKi", 50) / 100.0;
                healOrRevive(ally, pct, Json.bool(p, "revivesDead"));
                if (Json.bool(p, "alsoHealsCaster") && ally != f) {
                    healOrRevive(f, pct, false);
                }
                double extra = Json.num(p, "extraAlliesInRadiusPx", 0);
                if (extra > 0) {
                    for (Fighter o : squad) {
                        if (o != ally && o != f && o.alive && ally.distance(o) <= extra) {
                            healOrRevive(o, pct, false);
                        }
                    }
                }
                addEffect(Effect.REGEN, f, ally, 0, id);
                return true;
            }
            case 8 -> {                                                                    // Tái tạo năng lượng: heal over time
                pay(f, slot, cd, kiCost, allKi);
                f.regenUntil = now + (long) (rule("regen_skill_s", 15) * 1000);
                f.regenPctPerSec = Json.num(p, "regenPctPerSec", 4);
                f.regenAtX = f.x;
                f.regenAtY = f.y;
                addEffect(Effect.REGEN, f, f, (int) (rule("regen_skill_s", 15) * 1000), id);
                return true;
            }
            case 12 -> {                                                                   // Đẻ trứng: a pet fights for a while
                pay(f, slot, cd, kiCost, allKi);
                PetEntity pet = new PetEntity(nextId++, f);
                pet.x = f.x + 30;
                pet.y = f.y;
                pet.hpMax = pet.hp = Math.max(1, f.hpMax / 4);
                int[] hatched = {8, 11, 32, 25, 43, 49, 50};                // the mob of the pet by level of the skill (Nro: SkillUtil.getTempMobMe)
                pet.mobTemplateId = hatched[Math.max(0, Math.min(hatched.length, f.skillLevelBySlot.getOrDefault(slot, 1)) - 1)];
                pet.expiresAt = f.gen("MINION_PERSIST", 12) > 0 ? Long.MAX_VALUE : now + (long) Json.num(p, "lifetimeMs", 120000);      // GEN: it stays until it is beaten
                pet.damagePct = Json.num(p, "petDamagePctOfAttack", 30);
                pet.atk = f.atk;
                pet.critRatePct = 0;
                pet.armorReduction = 0;
                entities.add(pet);
                spawnBuf.add(spawnProto(pet));
                return true;
            }
            case 13 -> {                                                                   // Biến hình: a hold of 1.5 s, then the monkey (finishCast)
                if (now < f.monkeyUntil) {
                    return false;                                                          // already a monkey (Nro: the skill does nothing then)
                }
                pay(f, slot, cd, kiCost, allKi);
                long castMs = (long) Json.num(p, "castMs", 1500);
                beginCast(f, sk, L, castMs, target, f.dirX, f.dirY);
                addEffect(Effect.CAST_START, f, f, (int) castMs, id);
                return true;
            }
            case 19 -> {                                                                   // Khiên năng lượng
                pay(f, slot, cd, kiCost, allKi);
                long ms = (long) Json.num(p, "durationMs", 15000);
                f.shieldUntil = now + ms;
                addEffect(Effect.SHIELD, f, f, (int) ms, id);
                return true;
            }
            case 21 -> {                                                                   // Huýt sáo: more HP for the allies around
                pay(f, slot, cd, kiCost, allKi);
                double radius = L.radiusPx() == null ? 500 : L.radiusPx();
                for (Fighter o : squad) {
                    if (o.alive && o.distance(f) <= radius && o.hpBonusAmount == 0) {
                        long add = Math.round(o.hpMax * Json.num(p, "hpBonusPct", 40) / 100.0);
                        o.hpBonusAmount = add;
                        o.hpMax += add;
                        o.hp += add;
                        o.hpBonusUntil = now + (long) (Json.num(p, "displayedDurationSec", 30) * 1000);
                        if (Json.bool(p, "namecAlliesLose10PctHp") && o.race.equals("NAMEC") && o != f) {
                            o.hp = Math.max(1, o.hp - Math.round(o.hpMax * 0.10));
                        }
                        addEffect(Effect.BUFF, f, o, (int) (Json.num(p, "displayedDurationSec", 30) * 1000), id);
                    }
                }
                return true;
            }
            case 18, 20, 22, 23 -> {                                                       // target skills: chocolate, teleport, sleep, hold
                Entity t = target != null && target.alive && target.team == Team.ENEMY ? target : nearestEnemy(f, 1e9);
                double reach = L.radiusPx() == null ? 300 : L.radiusPx();
                if (t == null || f.distance(t) > Math.max(reach, id == 20 ? 5000 : reach)) {
                    return false;
                }
                pay(f, slot, cd, kiCost, allKi);
                applyTargetSkill(f, sk, L, t);
                return true;
            }
            default -> {
                return false;
            }
        }
    }

    private void applyTargetSkill(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L, Entity t) {
        Map<String, Object> p = L.params();
        int id = sk.id();
        aimAt(f, t, 0, 0);
        switch (id) {
            case 20 -> {                                                                   // Dịch chuyển tức thời: appear next to the target, stun it, the next hit is a crit
                f.x = t.x - f.dirX * (f.radius + t.radius + 4);
                f.y = t.y - f.dirY * (f.radius + t.radius + 4);
                hit(f, t, f.atk, id, false, true, false);                                   // Nro: the teleport itself strikes (the base attack, always a critical hit), then the stun
                long ms = (long) Json.num(p, "stunMs", 1000);
                stun(t, ms);
                f.nextHitCrit = Json.bool(p, "alwaysCrit");
                f.nextHitCritUntil = now + 4000;
                addEffect(Effect.TELEPORT, f, f, 0, id);
                addEffect(Effect.STUN, f, t, (int) controlMs(t, ms), id);
            }
            case 18 -> {                                                                   // Biến sôcôla: the target becomes a sweet and cannot act
                long ms = (long) Json.num(p, "durationMs", 30000);
                long eff = controlMs(t, ms);
                t.chocolateUntil = now + eff;
                addEffect(Effect.CHOCOLATE, f, t, (int) eff, id);
            }
            case 22 -> {                                                                   // Thôi miên: the target falls asleep (Nro: the target only, no area)
                long ms = (long) Json.num(p, "sleepMs", 5000);
                long eff = controlMs(t, ms);
                t.sleepUntil = now + eff;
                addEffect(Effect.SLEEP, f, t, (int) eff, id);
            }
            case 23 -> {                                                                   // Trói: the target cannot move while the caster stands there (Nro setUseTroi)
                long ms = (long) Json.num(p, "holdMs", 5000);
                if (t instanceof Fighter tf && tf.cast != null) {
                    break;                                                                 // a fighter that is charging cannot be tied (SkillService: prepareQCKK, prepareLaze, prepareTuSat)
                }
                long eff = controlMs(t, ms);
                t.holdUntil = now + eff;
                t.holdById = f.id;
                addEffect(Effect.HOLD, f, t, (int) eff, id);
            }
            default -> {
            }
        }
    }

    private void beginCast(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L, long ms, Entity target, double dx, double dy) {
        beginCastWithKi(f, sk, L, ms, target, dx, dy, f.ki);
    }

    private void beginCastWithKi(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L, long ms, Entity target, double dx, double dy, double kiAtCast) {
        f.cast = new Cast(sk, L, now + ms, target == null ? 0 : target.id, dx, dy, kiAtCast);
    }

    private void finishCast(Fighter f, Cast c) {
        StaticData.SkillLevel L = c.level;
        Map<String, Object> p = L.params();
        int id = c.skill.id();
        switch (id) {
            case 24, 25 -> {                                                               // the beam: every tickMs for activeMs it hits everything in the rectangle (Nro NewSkill)
                long active = (long) Json.num(p, "activeMs", 2000);
                long tickMs = Math.max(TICK_MS, (long) Json.num(p, "tickMs", 250));
                boolean spin = spinWanted(f, c);
                if (spin) {
                    active = Math.round(rule("dg_ult_spin_s", 4) * 1000);              // one turn in 4 seconds (owner request 2026-10-11)
                }
                double reach = L.lengthPx() == null ? 200 : L.lengthPx();
                Channel ch = new Channel(c.skill, L, now, now + active, tickMs, f.x, f.y, c.dirX, c.dirY);
                ch.nextTickAt = now + tickMs;
                if (spin) {
                    ch.spinRadPerMs = 2 * Math.PI / active;
                    ch.lastAngle = Math.atan2(c.dirY, c.dirX);
                    ch.spinLength = Math.min(reach, rule("dg_ult_spin_length", 800));
                }
                f.channel = ch;
                EffectEvent.Builder ev = EffectEvent.newBuilder().setEffect(Effect.ULTIMATE_BEAM).setSourceId(f.id).setTargetId(f.id).setDurationMs((int) active)
                        .setX((float) f.x).setY((float) f.y).setDirX((float) c.dirX).setDirY((float) c.dirY)
                        .setLength((float) (spin ? ch.spinLength : reach)).setWidth((float) (L.widthPx() == null ? 60 : L.widthPx())).setSkillId(id)
                        .setSpinDegPerS(spin ? (float) (360.0 * 1000.0 / active) : 0f);
                effectBuf.add(ev.build());
            }
            case 11 -> {                                                                   // Makankosappo: the laser hits the target it was aimed at, nothing else (Nro: plTarget or mobTarget)
                double length = L.lengthPx() == null ? 2000 : L.lengthPx();
                double base = c.kiAtCast * (L.damagePctMaxKi() == null ? 70 : L.damagePctMaxKi()) / 100.0;
                Entity aim = c.targetId == 0 ? null : find(c.targetId);
                if (aim == null || !aim.alive || aim.team == f.team) {
                    aim = nearestEnemy(f, length);
                }
                if (aim != null && f.distance(aim) <= length + aim.radius) {
                    hit(f, aim, base, id, false, false, false);
                }
            }
            case 26 -> mafuba(f, c, L, id);
            case 10 -> {                                                                   // 10% of the HP of each victim plus 10 x the caster's attack, around the target the ball lands on
                double radius = L.radiusPx() == null ? 380 : L.radiusPx();
                Entity aim = c.targetId == 0 ? null : find(c.targetId);
                if (aim == null || !aim.alive || aim.team == f.team) {
                    aim = nearestEnemy(f, 1e9);
                }
                if (aim == null) {
                    break;
                }
                double ax = aim.x;
                double ay = aim.y;
                for (Entity e : enemiesOf(f)) {
                    if (Math.hypot(e.x - ax, e.y - ay) <= radius + e.radius) {
                        hit(f, e, e.hp * 0.10 + 10 * f.atk, id, false, false, false);
                    }
                }
            }
            case 13 -> startMonkey(f, L);
            case 14 -> {                                                                   // Tự phát nổ: damage = the caster's current HP, ignores armor, halved against bosses; the caster dies
                double radius = (L.radiusPx() == null ? 430 : L.radiusPx()) + f.gen("SKILL_RADIUS_PX", 14);        // GEN: a wider blast
                double divisor = Json.num(p, "vsBossDamageDivisor", 2);
                double dmg = f.hp + f.hpMax * f.gen("SKILL_DMG_HPMAX_PCT", 14) / 100.0;                          // GEN: part of the maximum HP is added to the blast
                for (Entity e : enemiesOf(f)) {
                    if (f.distance(e) <= radius + e.radius) {
                        boolean boss = e instanceof Monster m && m.kind == MonsterKind.BOSS;
                        hit(f, e, boss ? dmg / divisor : dmg, id, true, false, true);
                    }
                }
                kill(f, f);
            }
            default -> {
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- the ultimates, the monkey, what a move ends

    /**
     * In a dungeon the beam of an ultimate turns once around the caster when the target in focus is a small monster (or, with nothing in focus, small monsters are about);
     * against a boss it stays a straight beam (owner request 2026-10-11).
     */
    private boolean spinWanted(Fighter f, Cast c) {
        if (!dungeon) {
            return false;
        }
        Entity aim = c.targetId == 0 ? null : find(c.targetId);
        if (aim instanceof Monster mm && mm.alive) {
            return mm.kind == MonsterKind.MINION;
        }
        for (Entity e : enemiesOf(f)) {
            if (e instanceof Monster mm && mm.kind == MonsterKind.MINION) {
                return true;
            }
        }
        return false;
    }

    /**
     * One tick of the beam of Super Kamejoko or Cađíc liên hoàn chưởng: everything in the rectangle, the one in focus and what lies behind it as well. A spinning beam hits what
     * is in its rectangle now and what it has swept over since the last tick, each at most once per tick.
     */
    private void ultimateTick(Fighter f, Channel ch) {
        int id = ch.skill.id();
        if (id != 24 && id != 25) {
            return;
        }
        double length = ch.level.lengthPx() == null ? 200 : ch.level.lengthPx();
        double width = ch.level.widthPx() == null ? 60 : ch.level.widthPx();
        double base = f.atk * pct(ch.level) / 100.0;
        if (ch.spinRadPerMs == 0) {
            for (Entity e : enemiesOf(f)) {
                if (inRect(ch.originX, ch.originY, ch.dirX, ch.dirY, length, width, e)) {
                    hit(f, e, base, id, false, false, false);
                }
            }
            return;
        }
        ch.tickIndex++;
        double a0 = ch.lastAngle;
        double a1 = Math.atan2(ch.dirY, ch.dirX) + ch.spinRadPerMs * ch.tickMs * ch.tickIndex;
        ch.lastAngle = a1;
        double sweep = a1 - a0;
        double dx = Math.cos(a1);
        double dy = Math.sin(a1);
        double reach = ch.spinLength;
        double twoPi = 2 * Math.PI;
        for (Entity e : enemiesOf(f)) {
            double dist = Math.hypot(e.x - ch.originX, e.y - ch.originY);
            if (dist > reach + e.radius) {
                continue;
            }
            boolean struck = inRect(ch.originX, ch.originY, dx, dy, reach, width, e) || dist <= e.radius + 30;
            if (!struck) {
                double delta = Math.atan2(e.y - ch.originY, e.x - ch.originX) - a0;
                delta -= Math.floor(delta / twoPi) * twoPi;                   // 0 to 2 pi: how far past the old direction the target stands
                struck = delta <= sweep;
            }
            if (struck) {
                hit(f, e, base, id, false, false, false);
            }
        }
    }

    /**
     * Ma phong ba (Nro SkillService.updateSkillSpecial, EffectSkillService.finishUseMafuba): when the gong ends everything within the range is pulled into the jar
     * (the client shows the targets spinning into it); gatherMs later each of them is a jar for jarMs, taking the caster's max HP x level % every tickMs (never
     * to death), unable to act the whole time.
     */
    private void mafuba(Fighter f, Cast c, StaticData.SkillLevel L, int id) {
        Map<String, Object> p = L.params();
        double radius = L.radiusPx() == null ? 500 : L.radiusPx();
        long active = (long) Json.num(p, "activeMs", 2000);
        long gather = (long) Json.num(p, "gatherMs", 4000);
        long jarMs = (long) Json.num(p, "jarMs", 11000);
        long tickMs = Math.max(TICK_MS, (long) Json.num(p, "tickMs", 500));
        long perTick = Math.max(1, Math.round(f.hpMax * Json.num(p, "hpPctPerLevel", 1) * Math.max(1, f.skillLevelBySlot.getOrDefault(c.skill.slot(), 1)) / 100.0));
        double jx = clamp(f.x + c.dirX / Math.max(1e-6, Math.hypot(c.dirX, c.dirY)) * Json.num(p, "jarDistancePx", 75), 0, arenaW);
        double jy = f.y;
        Channel hold = new Channel(c.skill, L, now, now + active, active, f.x, f.y, c.dirX, c.dirY);          // the caster stays in the pose while the jar works (no ticks)
        f.channel = hold;
        for (Entity e : enemiesOf(f)) {
            if (f.distance(e) > radius + e.radius) {
                continue;
            }
            long jar = controlMs(e, jarMs);
            e.jarFrom = now;
            e.jarGatherEnd = now + gather;
            e.jarUntil = now + gather + jar;
            if (e instanceof Fighter ef) {
                ef.cast = null;
                ef.channel = null;
            }
            effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.MAFUBA_GATHER).setSourceId(f.id).setTargetId(e.id).setDurationMs((int) gather).setX((float) jx).setY((float) jy)
                    .setSkillId(id).build());
            effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.JAR).setSourceId(f.id).setTargetId(e.id).setDurationMs((int) (gather + jar)).setX((float) e.x).setY((float) e.y)
                    .setLength((float) gather).setSkillId(id).build());
            jars.add(new Jar(e, f, perTick, now + gather, now + gather + jar, tickMs));
        }
    }

    private void updateJars() {
        for (Iterator<Jar> it = jars.iterator(); it.hasNext(); ) {
            Jar j = it.next();
            if (!j.victim.alive || now >= j.until) {
                it.remove();
                continue;
            }
            while (j.nextAt <= now) {
                long amount = Math.min(j.damage, Math.max(0, j.victim.hp - 1));          // the jar hurts but never kills (Nro: hp - 1 for a mob, hp - 100 for a player)
                j.nextAt += j.tickMs;
                if (amount <= 0) {
                    continue;
                }
                if (j.victim.shielded(now)) {
                    damageBuf.add(DamageEvent.newBuilder().setSourceId(j.owner.id).setTargetId(j.victim.id).setAbsorbed(true).setSkillId(26).build());
                    continue;
                }
                j.victim.hp -= amount;
                damageBuf.add(DamageEvent.newBuilder().setSourceId(j.owner.id).setTargetId(j.victim.id).setAmount(amount).setSkillId(26).build());
                if (j.owner instanceof Fighter sf) {
                    sf.damageDealt += amount;
                }
            }
        }
    }

    /** Biến hình (Nro EffectSkillService.setIsMonkey, NPoint): max HP + (level + 3) x 10 %, the HP now doubled, attack + (level + 3) %, a critical rate of 110. */
    private void startMonkey(Fighter f, StaticData.SkillLevel L) {
        Map<String, Object> p = L.params();
        long add = Math.round(f.hpMax * Json.num(p, "hpBonusPct", 40) / 100.0);
        f.monkeyHpAdd = add;
        f.hpMax += add;
        f.hp = Math.min(f.hpMax, f.hp * 2);
        f.monkeyAtkAdd = f.atk * Json.num(p, "dameBonusRaw", 4) / 100.0;
        f.atk += f.monkeyAtkAdd;
        f.monkeyCritBefore = f.critRatePct;
        f.critRatePct = Json.num(p, "critRatePct", 110);
        long ms = Math.round(Json.num(p, "durationMs", 60000) * (1 + f.gen("SKILL_DURATION_MULT", 13)));      // GEN: the monkey lasts longer
        f.monkeyUntil = now + ms;
        addEffect(Effect.BUFF, f, f, (int) ms, 13);
    }

    private void endMonkey(Fighter f) {
        if (f.monkeyUntil == 0) {
            return;
        }
        f.hpMax -= f.monkeyHpAdd;
        f.hp = Math.min(f.hp, f.hpMax);
        f.atk -= f.monkeyAtkAdd;
        f.critRatePct = f.monkeyCritBefore;
        f.monkeyHpAdd = 0;
        f.monkeyAtkAdd = 0;
        f.monkeyUntil = 0;
        addEffect(Effect.CAST_END, f, f, 0, 13);
    }

    /** Tái tạo năng lượng ends (a move, another skill, a stun, full HP and Ki): the client takes the pose off. */
    private void endRegen(Fighter f) {
        if (now < f.regenUntil) {
            f.regenUntil = 0;
            addEffect(Effect.CAST_END, f, f, 0, 8);
        }
    }

    /** What a fighter holds with Trói lets go (Nro removeUseTroi): it moved, used a skill, was stopped or died. */
    private void releaseHolds(Entity holder) {
        for (Entity e : entities) {
            if (e.holdById == holder.id) {
                e.holdById = 0;
                if (now < e.holdUntil) {
                    e.holdUntil = now;
                    addEffect(Effect.CAST_END, holder, e, 0, 23);
                }
            }
        }
    }

    private void breakChannel(Fighter f) {
        if (f.channel != null) {
            int id = f.channel.skill.id();
            f.channel = null;
            addEffect(Effect.CAST_END, f, f, 0, id);
        }
    }

    private void meleeRect(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L) {
        double length = L.lengthPx() == null ? 36 : L.lengthPx();
        double width = L.widthPx() == null ? 18 : L.widthPx();
        double base = f.atk * pct(L) / 100.0;
        boolean forced = f.nextHitCrit && now < f.nextHitCritUntil;
        boolean used = false;
        for (Entity e : enemiesOf(f)) {
            if (inRect(f.x, f.y, f.dirX, f.dirY, length, width, e)) {
                hit(f, e, base, sk.id(), false, forced, false);
                used = true;
            }
        }
        if (used && forced) {
            f.nextHitCrit = false;
        }
    }

    private void pay(Fighter f, int slot, long cooldownMs, double kiCost, boolean allKi) {
        f.ki = allKi ? 0 : Math.max(0, f.ki - kiCost);
        f.cooldownUntil.put(slot, now + cooldownMs);
    }

    private static double pct(StaticData.SkillLevel L) {
        return L.damagePctAtk() == null ? 100 : L.damagePctAtk();
    }

    private void aimAt(Fighter f, Entity target, double dx, double dy) {
        if (target != null) {
            normalizeInto(f, target.x - f.x, target.y - f.y);
        } else if (dx != 0 || dy != 0) {
            normalizeInto(f, dx, dy);
        }
    }

    private Fighter healTarget(Fighter caster, Entity target, StaticData.SkillLevel L) {
        double reach = L.radiusPx() == null ? 130 : L.radiusPx();
        if (target instanceof Fighter t && t.team == Team.PLAYER && caster.distance(t) <= reach + 40) {
            return t;
        }
        Fighter best = null;
        for (Fighter o : squad) {
            if (caster.distance(o) <= reach + 40 && (best == null || ratio(o) < ratio(best))) {
                best = o;
            }
        }
        return best;
    }

    private static double ratio(Entity e) {
        return e.alive ? (double) e.hp / Math.max(1, e.hpMax) : -1;
    }

    private void healOrRevive(Fighter t, double pct, boolean revives) {
        if (!t.alive) {
            if (!revives) {
                return;
            }
            t.alive = true;
            t.hp = 1;
            t.cast = null;
            spawnBuf.add(spawnProto(t));
        }
        t.hp = Math.min(t.hpMax, t.hp + Math.round(t.hpMax * pct));
        t.ki = Math.min(t.kiMax, t.ki + t.kiMax * pct);
    }

    // ---------------------------------------------------------------------------------------------------------------- damage

    /**
     * One hit (docs 7.4): base damage, critical x (1 + crit damage), +/-5% spread, then the target's armor, then shield. Minimum 1.
     * {@code undodgeable}: the hit cannot be dodged (boss focus skills, self-destruct).
     */
    private void hit(Entity src, Entity target, double base, int skillId, boolean ignoreArmor, boolean forceCrit, boolean undodgeable) {
        if (target == null || !target.alive) {
            return;
        }
        if (target.shielded(now)) {
            damageBuf.add(DamageEvent.newBuilder().setSourceId(src.id).setTargetId(target.id).setAbsorbed(true).setSkillId(skillId).build());
            return;
        }
        if (!undodgeable && target.dodgePct > 0 && rng.nextDouble() * 100.0 < target.dodgePct) {
            damageBuf.add(DamageEvent.newBuilder().setSourceId(src.id).setTargetId(target.id).setAbsorbed(true).setSkillId(skillId).build());
            return;
        }
        boolean crit = forceCrit || rng.nextDouble() * 100.0 < src.effectiveCritRate(now);
        Fighter genOwner = src instanceof Fighter sf ? sf : src instanceof PetEntity pe ? pe.owner : null;       // GEN: +% to the damage of this one skill
        double genDmg = genOwner == null ? 0 : genOwner.gen("SKILL_DMG_PCT", skillId);
        double dmg = genDmg == 0 ? base : base * (1 + genDmg / 100.0);
        if (crit) {
            dmg *= 1 + src.critDmgPct / 100.0;
        }
        double variance = rule("damage_variance_pct", 5) / 100.0;
        dmg *= 1 + (rng.nextDouble() * 2 - 1) * variance;
        if (!ignoreArmor) {
            double reduction = target.armorReduction;
            if (target instanceof Fighter tf && now < tf.buffUntil && tf.buffArmorMult != 1.0) {
                reduction = Math.min(d.balanceNum("armor_reduction_cap", 0.8), 1 - (1 - reduction) / tf.buffArmorMult);
            }
            dmg *= 1 - reduction;
        }
        long amount = Math.max(1, Math.round(dmg));
        long dealt = Math.min(amount, target.hp);
        target.hp -= dealt;
        if (target instanceof Dummy && target.hp <= 0) {
            target.hp = target.hpMax;                      // a training dummy gets all its HP back when it runs out
            addEffect(Effect.REGEN, target, target, 0, 0);
        }
        damageBuf.add(DamageEvent.newBuilder().setSourceId(src.id).setTargetId(target.id).setAmount(amount).setCrit(crit).setSkillId(skillId).build());
        if (src instanceof Fighter sf) {
            sf.damageDealt += dealt;
        } else if (src instanceof PetEntity pe) {
            pe.owner.damageDealt += dealt;
        }
        if (src.lifestealPct > 0) {
            heal(src, dealt * src.lifestealPct / 100.0);
        }
        if (src.kiStealPct > 0 && src.kiMax > 0) {
            src.ki = Math.min(src.kiMax, src.ki + dealt * src.kiStealPct / 100.0);
        }
        if (target.hp <= 0) {
            kill(src, target);
        }
    }

    private void kill(Entity killer, Entity victim) {
        if (!victim.alive) {
            return;
        }
        victim.alive = false;
        victim.hp = 0;
        double kx = killer == null || killer == victim ? 0 : victim.x - killer.x;
        double ky = killer == null || killer == victim ? 0 : victim.y - killer.y;
        double kl = Math.hypot(kx, ky);
        effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.DIED).setSourceId(killer == null ? 0 : killer.id).setTargetId(victim.id).setX((float) victim.x).setY((float) victim.y)
                .setDirX(kl < 1e-6 ? 0f : (float) (kx / kl)).setDirY(kl < 1e-6 ? 0f : (float) (ky / kl)).build());       // the client throws the body away from the blow
        releaseHolds(victim);
        telegraphs.removeIf(t -> t.ownerId() == victim.id);                               // the blow of a dead boss never lands: its warning goes with it (the client drops it on DIED)
        if (victim instanceof Fighter f) {
            f.cast = null;
            f.channel = null;
            f.regenUntil = 0;
            endMonkey(f);
            if (training != null) {
                delayed.add(new Delayed(now + training.reviveAfterMs(), () -> standUp(f), null));     // training: back on its feet after a moment
            }
        }
        if (victim instanceof Monster m) {
            onMonsterDeath(m, killer);
        }
    }

    /** Training: a character that died (Tự phát nổ) stands up again with full HP and Ki. */
    private void standUp(Fighter f) {
        if (f.alive || finished) {
            return;
        }
        f.alive = true;
        f.hp = f.hpMax;
        f.ki = f.kiMax;
        f.cast = null;
        f.stunUntil = f.holdUntil = f.sleepUntil = f.chocolateUntil = f.jarUntil = 0;
        f.channel = null;
        spawnBuf.add(spawnProto(f));
    }

    private void spawnDummy(MatchSetup.Dummy dm) {
        Dummy x = new Dummy(nextId++, dm.key(), dm.name(), dm.mobTemplateId(), dm.costumeId(), dm.radius());
        x.x = clamp(dm.x(), 0, arenaW);
        x.y = clamp(dm.y(), 0, arenaH);
        x.dirX = -1;
        x.hpMax = x.hp = Math.max(1, dm.hp());
        x.armorReduction = Math.max(0, Math.min(0.95, dm.armorPct() / 100.0));
        entities.add(x);
        spawnBuf.add(spawnProto(x));
    }

    private void onMonsterDeath(Monster m, Entity killer) {
        if (m.summoned) {
            return;                                          // called by a boss: no gold, no drops, no kill streak
        }
        boolean real = m.kind == MonsterKind.BOSS;
        kills.add(new MatchOutcome.Kill(m.classKey, m.boss.id(), real));
        Map<String, Object> split = d.balanceObj("gold_split");
        double winGold = d.balanceNum("gold_per_win_per_level", 1000) * tierLevel();
        // streak: one more kill when it follows the previous one within the window
        double windowMs = Json.num(d.balanceObj("exp_rules"), "streak_window_s", 5) * 1000;
        streak = now - lastKillAt <= windowMs ? streak + 1 : 1;
        lastKillAt = now;
        if (real) {
            bossesDefeated++;
            double hasMinions = setup.minionPool().isEmpty() ? 0 : 1;
            double bossShare = hasMinions == 0 ? 1.0 : Json.num(split, "boss_share", 0.6);
            goldEarned += Math.round(winGold * bossShare / Math.max(1, bossCount()));
            fastestBossKillS = Math.min(fastestBossKillS, (now - bossSpawnedAt) / 1000.0);
            double perBoss = expPerBoss();
            double bonus = streakBonus();
            for (Fighter f : squad) {
                double over = overlevelFactor(f.level);
                addExp(f.characterId, perBoss * over * (1 + bonus));
                if (f.partnerCharacterId != 0) {
                    addExp(f.partnerCharacterId, perBoss * overlevelFactor(partnerLevel(f)) * (1 + bonus));
                }
            }
            if (m.role.equals("FINAL")) {
                finalDefeated = true;
            }
            currentBoss = null;
            stageStarted = false;                            // the next stage begins with its waves
            bossReadyAt = 0;
            // wipe the remaining minions of this fight (the ones the boss called) and heal the squad a little before the next one
            for (Entity e : new ArrayList<>(entities)) {
                if (e instanceof Monster x && x.alive && x.kind == MonsterKind.MINION) {
                    x.alive = false;
                    x.hp = 0;
                    effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.DIED).setTargetId(x.id).setX((float) x.x).setY((float) x.y).build());
                }
            }
            if (finalDefeated) {
                end(MatchResult.WON);
            } else {
                nextBossAt = now + (long) (rule("boss_spawn_delay_s", 3) * 1000);
                double pctHeal = rule("between_boss_heal_pct", 15) / 100.0;
                for (Fighter f : squad) {
                    if (f.alive) {
                        heal(f, f.hpMax * pctHeal);
                    }
                }
            }
        } else {
            double weight = m.classKey.equals("ELITE") ? Json.num(split, "elite_weight", 5) : 1.0;
            double expectedWeight = Math.max(1, expectedWaveMinions()) * averagePoolWeight(split);
            goldEarned += Math.round(winGold * Json.num(split, "mob_share", 0.4) * weight / expectedWeight);
        }
        if (killer instanceof Fighter f) {
            f.kills++;
        }
    }

    /** The small monsters a match is expected to have (the waves of every stage, never above the cap of the match): the 40% of the gold of the match for them is shared out by this. */
    private double expectedWaveMinions() {
        int cap = (int) d.balanceNum("minion_cap_per_match", 20);
        double total = 0;
        for (int n : waveSizes()) {
            total += n;
        }
        return Math.min(cap, total * Math.max(1, bossCount()));
    }

    private double averagePoolWeight(Map<String, Object> split) {
        if (setup.minionPool().isEmpty()) {
            return 1;
        }
        double w = Json.num(split, "elite_weight", 5);
        double sum = 0;
        for (MatchSetup.Encounter e : setup.minionPool()) {
            sum += e.classKey().equals("ELITE") ? w : 1;
        }
        return sum / setup.minionPool().size();
    }

    private int partnerLevel(Fighter f) {
        return f.level;     // a fusion needs both characters at level 50 or more; EXP uses the main's level for the pair's overlevel factor
    }

    private void addExp(long characterId, double amount) {
        expRaw.merge(characterId, amount, Double::sum);
    }

    private double tierLevel() {
        return Json.num(Json.obj(rules, "tier_level"), setup.dungeon().tier(), 30);
    }

    /** EXP of one boss = EXP needed for level R / (1.2 x bosses x M) (docs/database/BattleBall.md 4.1). */
    private double expPerBoss() {
        Map<String, Object> e = d.balanceObj("exp_rules");
        double r = tierLevel();
        double need = Json.num(e, "coef", 100) * Math.pow(r, Json.num(e, "power", 2));
        return need / (1.2 * Math.max(1, bossCount()) * Json.num(e, "wins_per_level", 3));
    }

    /** Over-level: no penalty up to 5 levels above the dungeon, then 5% less per level down to 10%; a bonus up to +20% when far below. */
    double overlevelFactor(int level) {
        Map<String, Object> e = d.balanceObj("exp_rules");
        double diff = level - tierLevel();
        double free = Json.num(e, "overlevel_free", 5);
        if (diff > free) {
            return Math.max(Json.num(e, "overlevel_floor", 0.1), 1 - Json.num(e, "overlevel_step", 0.05) * (diff - free));
        }
        double under = Json.num(e, "underlevel_free", 10);
        if (diff < -under) {
            return 1 + Math.min(Json.num(e, "underlevel_cap", 0.2), Json.num(e, "underlevel_step", 0.02) * (-diff - under));
        }
        return 1.0;
    }

    private double streakBonus() {
        double bonus = 0;
        Object tiers = d.balanceObj("exp_rules").get("streak_tiers");
        if (tiers instanceof List<?> list) {
            for (Object t : list) {
                if (t instanceof List<?> pair && pair.size() == 2 && streak >= ((Double) pair.get(0))) {
                    bonus = (Double) pair.get(1);
                }
            }
        }
        return bonus;
    }

    // ---------------------------------------------------------------------------------------------------------------- allies and pets

    private void allyAct(Fighter f, double dt) {
        if (!f.canAct(now)) {
            return;
        }
        avoidTelegraphs(f, dt);
        // each AI character fights what is nearest to it (training: the client walks it to the nearest dummy in view); in a dungeon only what is on the screen of the player,
        // and it does not follow him (owner request 2026-10-11)
        Entity target = dungeon ? nearestEnemyInView(f) : nearestEnemy(f, 1e9);
        if (target == null) {
            return;
        }
        Map<String, Object> order = d.balanceObj("ai_skill_order");
        List<?> slots = order.get("slots") instanceof List<?> l ? l : List.of(8.0, 7.0, 6.0, 5.0, 4.0, 3.0, 2.0, 1.0, 0.0);
        List<?> never = order.get("never_use_skill_ids") instanceof List<?> n ? n : List.of();
        for (Object s : slots) {
            int slot = ((Double) s).intValue();
            StaticData.Skill sk = f.skillBySlot.get(slot);
            if (sk == null || never.contains((double) sk.id()) || !aiWants(f, sk, f.skillLevelBySlot.get(slot), target)) {
                continue;
            }
            if (tryUseSkill(f, slot, target.id, target.x - f.x, target.y - f.y)) {
                return;
            }
        }
        // nothing to cast: walk to the target so that the basic punch reaches it
        StaticData.Skill punch = f.skillBySlot.get(0);
        double reach = punch == null ? 30 : punchReach(punch, f.skillLevelBySlot.get(0)) - 4;
        double dist = f.distance(target) - target.radius;
        if (dist > reach && f.canMove(now) && training == null) {               // training: the client's village walks the AI
            moveToward(f, target.x, target.y, rule("ally_speed", 200) * dt);
        }
        normalizeInto(f, target.x - f.x, target.y - f.y);
    }

    private double punchReach(StaticData.Skill sk, int level) {
        StaticData.SkillLevel L = sk.level(level);
        return L == null || L.lengthPx() == null ? 36 : L.lengthPx();
    }

    /** Is it sensible for an AI ally to use this skill now? (the order of preference comes from the data; this only avoids wasting skills) */
    private boolean aiWants(Fighter f, StaticData.Skill sk, int level, Entity target) {
        StaticData.SkillLevel L = sk.level(level);
        if (L == null) {
            return false;
        }
        double dist = f.distance(target);
        boolean bossAlive = training != null || dungeon || (currentBoss != null && currentBoss.alive);     // a training dummy counts as the boss of the fight; in a dungeon the target is on the screen
        return switch (sk.id()) {
            case 0, 2, 4, 9, 17 -> dist - target.radius <= (L.lengthPx() == null ? 36 : L.lengthPx()) - 2 && (sk.id() != 9 || f.hp > f.hpMax * 0.4);
            case 1, 3, 5 -> dist <= (L.lengthPx() == null ? 300 : L.lengthPx()) + target.radius - 20;
            case 24, 25, 11 -> dist <= (L.lengthPx() == null ? 200 : Math.min(L.lengthPx(), 600)) * 0.8;
            case 26 -> dist <= (L.radiusPx() == null ? 500 : L.radiusPx()) * 0.8;
            case 10 -> bossAlive && dist <= (L.radiusPx() == null ? 380 : L.radiusPx()) * 0.8;
            case 6 -> enemiesOf(f).stream().anyMatch(e -> f.distance(e) <= (L.radiusPx() == null ? 150 : L.radiusPx()) && e.canAct(now));
            case 7 -> squad.stream().anyMatch(o -> (!o.alive || ratio(o) < 0.6) && f.distance(o) <= (L.radiusPx() == null ? 130 : L.radiusPx()) + 40);
            case 8 -> ratio(f) < 0.7 && now >= f.regenUntil;
            case 12 -> bossAlive && dist <= 600;
            case 13 -> bossAlive && now >= f.monkeyUntil;
            case 19 -> ratio(f) < 0.5 || telegraphs.stream().anyMatch(t -> inTelegraph(t, f));
            case 21 -> bossAlive && squad.stream().filter(o -> o.alive).count() > 1 && f.hpBonusAmount == 0;
            case 18, 22, 23 -> bossAlive && (target instanceof Dummy || target instanceof Monster m && m.kind == MonsterKind.BOSS)
                    && dist <= (L.radiusPx() == null ? 300 : L.radiusPx()) && target.canMove(now);
            case 20 -> bossAlive && dist > 220 && target.canAct(now);
            default -> false;
        };
    }

    private void avoidTelegraphs(Fighter f, double dt) {
        if (!f.canMove(now) || f.cast != null) {
            return;
        }
        for (TelegraphArea t : telegraphs) {
            if (inTelegraph(t, f)) {
                // step sideways out of the danger zone
                double px = -t.dirY();
                double py = t.dirX();
                double side = ((f.x - t.x()) * px + (f.y - t.y()) * py) >= 0 ? 1 : -1;
                moveToward(f, f.x + px * side * 200, f.y + py * side * 200, rule("ally_speed", 200) * dt);
                return;
            }
        }
    }

    private boolean inTelegraph(TelegraphArea t, Entity e) {
        return t.rect() ? inRect(t.x(), t.y(), t.dirX(), t.dirY(), t.length(), t.width(), e) : Math.hypot(e.x - t.x(), e.y - t.y()) <= t.length() + e.radius;
    }

    private void updatePet(PetEntity p) {
        if (now >= p.expiresAt || !p.owner.alive) {
            p.alive = false;
            return;
        }
        Entity t = nearestEnemy(p, 400);
        if (t == null) {
            moveToward(p, p.owner.x + 30, p.owner.y, rule("ally_speed", 200) * TICK_MS / 1000.0);
            return;
        }
        if (p.distance(t) > p.radius + t.radius + 20) {
            moveToward(p, t.x, t.y, rule("ally_speed", 200) * TICK_MS / 1000.0);
        } else if (now >= p.nextHitAt) {
            p.nextHitAt = now + 1000;
            hit(p, t, p.owner.atk * p.damagePct / 100.0, 12, false, false, false);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- encounters and monsters

    private void updateEncounters(double dt) {
        if (finished) {
            return;
        }
        boolean hasMinions = !setup.minionPool().isEmpty();
        List<Integer> waves = hasMinions ? waveSizes() : List.of();
        if (currentBoss == null && encounterIdx < setup.encounters().size()) {
            if (!stageStarted) {
                stageStarted = true;                         // a new stage: the waves of small monsters come first, the boss after the last of them (owner request 2026-10-11)
                waveIdx = 0;
                pendingSpawns = 0;
                firstWaveAt = now + (long) (rule("dg_first_wave_s", 2) * 1000);
                lastWaveAt = firstWaveAt;
                bossReadyAt = 0;
            }
            if (!waves.isEmpty()) {
                updateWaves(waves);
            }
            boolean wavesDone = waves.isEmpty() || (waveIdx >= waves.size() && pendingSpawns == 0 && aliveMinions() == 0);
            if (!wavesDone) {
                bossReadyAt = 0;
            } else {
                if (bossReadyAt == 0) {
                    bossReadyAt = now + (long) (rule("boss_spawn_delay_s", 3) * 1000);
                }
                if (now >= bossReadyAt && now >= nextBossAt) {
                    bossReadyAt = 0;
                    spawnBoss(setup.encounters().get(encounterIdx++));
                }
            }
        }
        if (currentBoss != null && currentBoss.alive && hasMinions) {
            updateSummons();
        }
    }

    /** The sizes of the waves of a stage (balance_config match_rules dg_waves). */
    private List<Integer> waveSizes() {
        List<Integer> out = new ArrayList<>();
        if (rules.get("dg_waves") instanceof List<?> l) {
            for (Object o : l) {
                if (o instanceof Number n && n.intValue() > 0) {
                    out.add(n.intValue());
                }
            }
        }
        return out.isEmpty() ? List.of(12, 15, 18, 20, 25) : out;
    }

    private int aliveMinions() {
        int n = 0;
        for (Entity e : entities) {
            if (e instanceof Monster m && m.alive && m.kind == MonsterKind.MINION) {
                n++;
            }
        }
        return n;
    }

    /**
     * The waves of small monsters of a stage (owner request 2026-10-11: "tấn công ồ ạt chia thành nhiều đợt"): a wave is let in when the one before it has fewer than
     * dg_wave_next_alive_le monsters left or dg_wave_next_after_s seconds have passed, and it rushes in from outside the screen over dg_wave_spawn_s seconds; never more than
     * dg_alive_cap alive at once, and never more than minion_cap_per_match in the match.
     */
    private void updateWaves(List<Integer> waves) {
        int alive = aliveMinions();
        int cap = (int) rule("dg_alive_cap", 30);
        int totalCap = (int) d.balanceNum("minion_cap_per_match", 20);
        if (waveIdx < waves.size() && pendingSpawns == 0) {
            boolean due = waveIdx == 0 ? now >= firstWaveAt
                    : alive <= rule("dg_wave_next_alive_le", 5) || now >= lastWaveAt + (long) (rule("dg_wave_next_after_s", 10) * 1000);
            if (due) {
                int size = Math.min(waves.get(waveIdx), totalCap - minionsSpawned);
                if (size <= 0) {
                    waveIdx = waves.size();                  // the cap of the match is reached: no more waves
                } else {
                    waveIdx++;
                    waveSize = size;
                    pendingSpawns = size;
                    lastWaveAt = now;
                    effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.WAVE).setSkillId(waveIdx).setLength(waves.size()).setWidth(size).build());
                }
            }
        }
        if (pendingSpawns > 0) {
            int perTick = Math.max(1, (int) Math.ceil(waveSize / (rule("dg_wave_spawn_s", 1.5) * 1000.0 / TICK_MS)));
            int n = Math.min(Math.min(pendingSpawns, perTick), cap - alive);
            for (int i = 0; i < n; i++) {
                double[] at = waveSpawnPoint();
                spawnMinion(setup.minionPool().get(rng.nextInt(setup.minionPool().size())), at[0], at[1], false);
                pendingSpawns--;
            }
        }
    }

    /** Where a monster of a wave comes in: just outside the screen, on the left or the right edge (a side that has room), anywhere across its height. */
    private double[] waveSpawnPoint() {
        double[] v = view();
        boolean leftRoom = v[0] > 60;
        boolean rightRoom = v[2] < arenaW - 60;
        double margin = 30 + rng.nextDouble() * 50;
        double x;
        if (leftRoom && rightRoom) {
            x = rng.nextBoolean() ? v[0] - margin : v[2] + margin;
        } else if (rightRoom) {
            x = v[2] + margin;
        } else if (leftRoom) {
            x = v[0] - margin;
        } else {
            x = rng.nextBoolean() ? 0 : arenaW;
        }
        double y = v[1] + rng.nextDouble() * (v[3] - v[1]);
        return new double[] {clamp(x, 0, arenaW), clamp(y, 20, arenaH - 20)};
    }

    /** The bosses of the match call small monsters while they fight: dg_summon_first when they appear, then dg_summon_batch every dg_summon_every_s seconds, within the caps. */
    private void updateSummons() {
        if (now < nextSummonAt) {
            return;
        }
        int alive = 0;
        for (Entity e : entities) {
            if (e instanceof Monster m && m.alive && m.summoned) {
                alive++;
            }
        }
        int batch = summonedThisBoss == 0 ? (int) rule("dg_summon_first", 5) : (int) rule("dg_summon_batch", 3);
        int totalCap = (int) d.balanceNum("minion_cap_per_match", 20);
        nextSummonAt = now + (long) (rule("dg_summon_every_s", 8) * 1000);
        int n = Math.min(Math.min(batch, (int) rule("dg_summon_alive_cap", 8) - alive), Math.min((int) rule("dg_summon_cap_per_boss", 20) - summonedThisBoss, totalCap - minionsSpawned));
        for (int i = 0; i < n; i++) {
            double ang = rng.nextDouble() * Math.PI * 2;
            Monster m = spawnMinion(setup.minionPool().get(rng.nextInt(setup.minionPool().size())), currentBoss.x + Math.cos(ang) * 120, currentBoss.y + Math.sin(ang) * 120, true);
            summonedThisBoss++;
            effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.SUMMON).setSourceId(currentBoss.id).setTargetId(m.id).setX((float) m.x).setY((float) m.y).build());
        }
    }

    private void spawnBoss(MatchSetup.Encounter enc) {
        StaticData.MonsterStat ms = d.monsterStat(enc.classKey(), setup.dungeon().tier());
        Monster m = new Monster(nextId++, enc.boss().name(), MonsterKind.BOSS, enc.boss(), enc.classKey(), enc.role(), rule("boss_radius", 34));
        applyMonsterStats(m, ms, enc.boss());
        m.planet = forcedPlanet != null ? forcedPlanet : PLANETS[rng.nextInt(PLANETS.length)];       // every boss draws a planet for the match and fights with its skills
        double[] v = view();
        m.x = clamp(v[2] - 60, 100, arenaW - 100);                  // it appears on the screen, at its right edge
        m.y = clamp((v[1] + v[3]) / 2, 80, arenaH - 80);
        m.homeX = m.x;
        m.homeY = m.y;
        m.dirX = -1;
        m.gapS = ms == null || ms.actionGapS() == null ? 1.8 : ms.actionGapS();
        m.nextActionAt = now + 1500;
        for (Map<String, Object> a : bossActions()) {
            m.actionReadyAt.put((String) a.get("name"), now + (long) (Json.num(a, "cooldown_s", 0) * 300));   // not everything at once at the start
        }
        entities.add(m);
        spawnBuf.add(spawnProto(m));
        effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.BOSS_COMING).setSourceId(m.id).setTargetId(m.id).setX((float) m.x).setY((float) m.y)
                .setSkillId(encounterIdx).setLength(setup.encounters().size()).build());
        currentBoss = m;
        bossSpawnedAt = now;
        summonedThisBoss = 0;
        nextSummonAt = now + 500;
    }

    private Monster spawnMinion(MatchSetup.Encounter enc, double x, double y, boolean summoned) {
        StaticData.MonsterStat ms = d.monsterStat(enc.classKey(), setup.dungeon().tier());
        Monster m = new Monster(nextId++, enc.boss().name(), MonsterKind.MINION, enc.boss(), enc.classKey(), "MINION", rule("minion_radius", 18));
        applyMonsterStats(m, ms, enc.boss());
        m.planet = enc.boss().race() == null ? "EARTH" : enc.boss().race();
        m.summoned = summoned;
        m.x = clamp(x, 0, arenaW);
        m.y = clamp(y, 0, arenaH);
        m.homeX = m.x;
        m.homeY = m.y;
        m.shootReadyAt = now + (long) (rule("dg_minion_shoot_min_s", 2.5) * rng.nextDouble() * 1000);      // they do not all shoot at once
        m.nextPunchAt = now + 400;
        entities.add(m);
        spawnBuf.add(spawnProto(m));
        minionsSpawned++;
        return m;
    }

    private void applyMonsterStats(Monster m, StaticData.MonsterStat ms, StaticData.Boss boss) {
        if (ms == null) {
            m.hpMax = m.hp = 1_000_000;
            m.atkPerHit = 10_000;
            m.armorReduction = 0.2;
            return;
        }
        m.hpMax = m.hp = Math.max(1, Math.round(ms.hp() * boss.hpMult()));
        double atkPct = ms.atkMin() + rng.nextDouble() * (ms.atkMax() - ms.atkMin());
        m.atkPerHit = atkPct / 100.0 * d.balanceNum("max_atk_total", 500_000) * boss.atkMult();
        m.atk = m.atkPerHit;
        m.armorReduction = (ms.armorMin() + ms.armorMax()) / 2.0 / 100.0;
        m.critRatePct = 0;
    }

    @SuppressWarnings("unchecked")
    private List<Map<String, Object>> bossActions() {
        Object o = d.balance().get("boss_actions");
        List<Map<String, Object>> out = new ArrayList<>();
        if (o instanceof List<?> l) {
            for (Object x : l) {
                if (x instanceof Map<?, ?> m) {
                    out.add((Map<String, Object>) m);
                }
            }
        }
        return out;
    }

    private void updateMonster(Monster m, double dt) {
        if (!m.canAct(now)) {
            return;
        }
        if (m.kind == MonsterKind.MINION) {
            minionAct(m, dt);
            return;
        }
        bossAct(m, dt);
    }

    /**
     * A small monster of a dungeon (owner request 2026-10-11): it rushes at the nearest character, shoots the chuong of level 1 of its planet when the target is in range and
     * the shot is ready (at most dg_shooter_cap shots in the air at once, aimed at where the target is: they can be dodged), and punches when it is close.
     */
    private void minionAct(Monster m, double dt) {
        Entity t = nearestPlayerSide(m);
        if (t == null) {
            return;
        }
        normalizeInto(m, t.x - m.x, t.y - m.y);
        if (now < m.actionUntil) {
            return;                                          // aiming or just shot: it stands still
        }
        double dist = m.distance(t);
        if (dist <= rule("dg_minion_melee_range", 40) + t.radius) {
            if (now >= m.nextPunchAt) {
                m.nextPunchAt = now + (long) (rule("dg_minion_punch_s", 1.2) * 1000);
                hit(m, t, m.atkPerHit * rule("dg_minion_dmg_mult", 0.1) * rule("dg_minion_punch_mult", 1.0), planetPunch(m.planet), false, false, false);
            }
            return;
        }
        if (now >= m.shootReadyAt && dist <= rule("dg_minion_shoot_range", 260) + t.radius && minionShots() < rule("dg_shooter_cap", 6)) {
            minionShoot(m, t);
            return;
        }
        if (m.canMove(now)) {
            moveToward(m, t.x, t.y, rule("dg_minion_speed", rule("minion_speed", 170)) * dt);
            separate(m);
        }
    }

    private static int planetPunch(String planet) {
        return planet.equals("NAMEC") ? 2 : planet.equals("XAYDA") ? 4 : 0;
    }

    private static int planetBeam(String planet) {
        return planet.equals("NAMEC") ? 3 : planet.equals("XAYDA") ? 5 : 1;
    }

    private long minionShots() {
        return projectiles.stream().filter(p -> p.owner instanceof Monster m && m.kind == MonsterKind.MINION).count();
    }

    private void minionShoot(Monster m, Entity t) {
        int skill = planetBeam(m.planet);
        StaticData.Skill sk = d.skills().get(skill);
        StaticData.SkillLevel L = sk == null ? null : sk.level(1);
        double speed = L == null || L.projectileSpeed() == null ? 480 : L.projectileSpeed();
        double range = L == null || L.lengthPx() == null ? 300 : L.lengthPx();
        double base = m.atkPerHit * rule("dg_minion_dmg_mult", 0.1) * rule("dg_minion_shoot_mult", 1.5);
        projectiles.add(new Projectile(nextProjectileId++, m, m.x, m.y, m.dirX, m.dirY, speed, range, rule("dg_minion_shot_radius", 24), base, skill, false, false));
        double lo = rule("dg_minion_shoot_min_s", 2.5);
        double hi = Math.max(lo, rule("dg_minion_shoot_max_s", 3.5));
        m.shootReadyAt = now + (long) ((lo + rng.nextDouble() * (hi - lo)) * 1000);
        m.actionUntil = now + (long) (rule("dg_minion_cast_s", 0.5) * 1000);
    }

    /** Small monsters do not pile up on one spot: two that overlap are pushed apart. */
    private void separate(Monster m) {
        for (Entity o : entities) {
            if (o != m && o instanceof Monster && o.alive) {
                double dx = m.x - o.x;
                double dy = m.y - o.y;
                double dist = Math.hypot(dx, dy);
                double min = m.radius + o.radius;
                if (dist < min && dist > 1e-6) {
                    double push = (min - dist) * 0.5;
                    m.x = clamp(m.x + dx / dist * push, 0, arenaW);
                    m.y = clamp(m.y + dy / dist * push, 0, arenaH);
                }
            }
        }
    }

    private void bossAct(Monster m, double dt) {
        double hpRatio = (double) m.hp / Math.max(1, m.hpMax);
        if (!m.enraged && hpRatio < rule("enrage_hp_pct", 30) / 100.0) {
            m.enraged = true;
            addEffect(Effect.ENRAGE, m, m, 0, 0);
        }
        Entity target = nearestPlayerSide(m);
        if (target == null) {
            return;
        }
        // leash (GDD 7.5): when the target leaves the zone the boss chases a little further, then goes home and heals
        double homeDist = Math.hypot(target.x - m.homeX, target.y - m.homeY);
        double leash = rule("leash_radius", 700);
        if (m.returning) {
            heal(m, m.hpMax * rule("leash_heal_pct_per_s", 5) / 100.0 * dt);
            if (m.canMove(now)) {
                moveToward(m, m.homeX, m.homeY, rule("boss_speed", 150) * 1.5 * dt);
            }
            if (Math.hypot(m.x - m.homeX, m.y - m.homeY) < 25 && homeDist <= leash) {
                m.returning = false;
                m.leashOvershoot = 0;
            }
            return;
        }
        if (homeDist > leash) {
            m.leashOvershoot += rule("boss_speed", 150) * dt;
            if (m.leashOvershoot > rule("leash_extra", 200)) {
                m.returning = true;
                return;
            }
        } else {
            m.leashOvershoot = 0;
        }
        double meleeRange = rule("boss_melee_range", 60);
        if (now >= m.nextActionAt) {
            double gap = m.gapS * (m.enraged ? rule("enrage_gap_factor", 0.7) : 1.0);
            if (performBossAction(m, target, meleeRange)) {
                m.nextActionAt = now + (long) (gap * 1000);
            }
        }
        // walk toward the target until the basic punch reaches it
        if (m.distance(target) > meleeRange + target.radius - 8 && m.canMove(now)) {
            moveToward(m, target.x, target.y, rule("boss_speed", 150) * dt);
        }
        normalizeInto(m, target.x - m.x, target.y - m.y);
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> kitOf(Monster m) {
        if (d.balance().get("boss_planet_kits") instanceof Map<?, ?> all && all.get(m.planet) instanceof Map<?, ?> kit) {
            return (Map<String, Object>) kit;
        }
        return Map.of();
    }

    /** The skill id a boss uses for this move: the one of its planet (the client draws the move of that skill), or the fallback. */
    private int kitSkill(Monster m, String key, int fallback) {
        return (int) Json.num(kitOf(m), key, fallback);
    }

    /** Does the planet of this boss have the move? Without a kit every move is there. */
    private boolean kitHas(Monster m, String action) {
        return !(kitOf(m).get("actions") instanceof List<?> l) || l.isEmpty() || l.contains(action);
    }

    /**
     * One boss action: the first ready entry of the priority list that the planet of the boss has and that makes sense now (docs 7.5; the planets: docs 15.25). Returns false
     * when nothing was possible.
     */
    private boolean performBossAction(Monster m, Entity target, double meleeRange) {
        for (Map<String, Object> a : bossActions()) {
            String name = (String) a.get("name");
            if (!kitHas(m, name)) {
                continue;
            }
            double cd = Json.num(a, "cooldown_s", 0);
            if (name.equals("stun") && m.enraged) {
                cd = Math.min(cd, rule("enrage_stun_cooldown_s", 12));
            }
            if (now < m.actionReadyAt.getOrDefault(name, 0L)) {
                continue;
            }
            double mult = Json.num(a, "damage_mult", 1.0);
            double base = m.atkPerHit * mult;
            switch (name) {
                case "ultimate" -> {
                    if (m.distance(target) > 600) {
                        continue;
                    }
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    bossUltimate(m, target, base, kitSkill(m, "ultimate", 24));
                    return true;
                }
                case "shield" -> {
                    if ((double) m.hp / m.hpMax > 0.8) {
                        continue;
                    }
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    long ms = (long) (rule("boss_shield_s", 10) * 1000);
                    m.shieldUntil = now + ms;
                    addEffect(Effect.SHIELD, m, m, (int) ms, 0);
                    return true;
                }
                case "stun" -> {
                    double radius = rule("boss_stun_radius", 300);
                    if (squad.stream().noneMatch(f -> f.alive && m.distance(f) <= radius)) {
                        continue;
                    }
                    int skill = kitSkill(m, "stun", 6);
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    for (Fighter f : squad) {
                        if (f.alive && m.distance(f) <= radius + f.radius) {
                            hit(m, f, base, skill, false, false, false);
                            stun(f, (long) (rule("boss_stun_s", 2) * 1000));
                            addEffect(Effect.STUN, m, f, (int) (rule("boss_stun_s", 2) * 1000), skill);
                        }
                    }
                    return true;
                }
                case "bomb" -> {
                    if (m.distance(target) > 900) {
                        continue;
                    }
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    bossBomb(m, target, base, kitSkill(m, "bomb", 10));
                    return true;
                }
                case "focus" -> {
                    Fighter top = null;
                    for (Fighter f : squad) {
                        if (f.alive && (top == null || f.hp > top.hp)) {
                            top = f;
                        }
                    }
                    if (top == null) {
                        continue;
                    }
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    hit(m, top, base, kitSkill(m, "focus", 0), false, false, true);          // locks on the unit with the most HP; cannot be dodged
                    return true;
                }
                case "beam" -> {
                    double range = rule("boss_beam_range", 700);
                    if (m.distance(target) > range + 100) {
                        continue;
                    }
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    normalizeInto(m, target.x - m.x, target.y - m.y);
                    projectiles.add(new Projectile(nextProjectileId++, m, m.x, m.y, m.dirX, m.dirY, rule("boss_beam_speed", 480), range,
                            rule("boss_beam_width", 60) / 2, base, kitSkill(m, "beam", 1), false, false));
                    return true;
                }
                default -> {                                                                // punch
                    if (m.distance(target) > meleeRange + target.radius) {
                        continue;
                    }
                    normalizeInto(m, target.x - m.x, target.y - m.y);
                    hit(m, target, base, kitSkill(m, "punch", 0), false, false, false);
                    return true;
                }
            }
        }
        return false;
    }

    private void bossUltimate(Monster m, Entity target, double base, int skill) {
        normalizeInto(m, target.x - m.x, target.y - m.y);
        long ms = (long) (rule("boss_ultimate_telegraph_s", 2) * 1000);
        double length = rule("boss_ultimate_length", 500);
        double width = rule("boss_ultimate_width", 160);
        double ox = m.x;
        double oy = m.y;
        double dx = m.dirX;
        double dy = m.dirY;
        telegraphs.add(new TelegraphArea(m.id, true, ox, oy, dx, dy, length, width, now + ms + 200));
        effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.TELEGRAPH_RECT).setSourceId(m.id).setDurationMs((int) ms).setX((float) ox).setY((float) oy)
                .setDirX((float) dx).setDirY((float) dy).setLength((float) length).setWidth((float) width).setSkillId(skill).build());
        delayed.add(new Delayed(now + ms, () -> {
            for (Fighter f : squad) {
                if (f.alive && inRect(ox, oy, dx, dy, length, width, f)) {
                    hit(m, f, base, skill, false, false, false);
                }
            }
        }, m));
    }

    /** The Quả cầu kênh khí of an Earth boss: a circle appears under the target and blows up after a moment (the characters step out of it). */
    private void bossBomb(Monster m, Entity target, double base, int skill) {
        long ms = (long) (rule("dg_bomb_telegraph_s", 2.5) * 1000);
        double radius = rule("dg_bomb_radius", 380);
        double ox = target.x;
        double oy = target.y;
        telegraphs.add(new TelegraphArea(m.id, false, ox, oy, 0, 0, radius, 0, now + ms + 200));
        effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.TELEGRAPH_CIRCLE).setSourceId(m.id).setDurationMs((int) ms).setX((float) ox).setY((float) oy)
                .setLength((float) radius).setSkillId(skill).build());
        delayed.add(new Delayed(now + ms, () -> {
            for (Fighter f : squad) {
                if (f.alive && Math.hypot(f.x - ox, f.y - oy) <= radius + f.radius) {
                    hit(m, f, base, skill, false, false, false);
                }
            }
        }, m));
    }

    // ---------------------------------------------------------------------------------------------------------------- projectiles

    private void updateProjectiles(double dt) {
        for (Iterator<Projectile> it = projectiles.iterator(); it.hasNext(); ) {
            Projectile p = it.next();
            double step = Math.min(p.remaining, p.speed * dt);
            p.x += p.dirX * step;
            p.y += p.dirY * step;
            p.remaining -= step;
            for (Entity e : p.team == Team.PLAYER ? enemiesOfTeam(Team.PLAYER) : playerSide()) {
                if (e.alive && !p.alreadyHit.contains(e.id) && Math.hypot(e.x - p.x, e.y - p.y) <= p.radius + e.radius) {
                    p.alreadyHit.add(e.id);
                    boolean forced = p.forceCrit || (p.owner instanceof Fighter f && f.nextHitCrit && now < f.nextHitCritUntil);
                    hit(p.owner, e, p.baseDamage, p.skillId, p.ignoreArmor, forced, false);
                    if (p.owner instanceof Fighter f && forced) {
                        f.nextHitCrit = false;
                    }
                }
            }
            if (p.remaining <= 0 || p.x < -50 || p.y < -50 || p.x > arenaW + 50 || p.y > arenaH + 50) {
                it.remove();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- geometry and helpers

    private List<Entity> enemiesOf(Entity e) {
        return enemiesOfTeam(e.team);
    }

    private List<Entity> enemiesOfTeam(Team mine) {
        List<Entity> out = new ArrayList<>();
        for (Entity e : entities) {
            if (e.alive && e.team != mine) {
                out.add(e);
            }
        }
        return out;
    }

    private List<Entity> playerSide() {
        List<Entity> out = new ArrayList<>();
        for (Entity e : entities) {
            if (e.alive && e.team == Team.PLAYER) {
                out.add(e);
            }
        }
        return out;
    }

    private Entity nearestEnemy(Entity from, double maxDist) {
        Entity best = null;
        double bd = maxDist;
        for (Entity e : enemiesOf(from)) {
            double dist = from.distance(e);
            if (dist <= bd) {
                best = e;
                bd = dist;
            }
        }
        return best;
    }

    /** What an AI character fights: the nearest enemy on the screen of the player, or one that is on it (dg_ai_defend_px): it defends itself but does not chase what is out of sight. */
    private Entity nearestEnemyInView(Entity from) {
        double[] v = view();
        double defend = rule("dg_ai_defend_px", 220);
        Entity best = null;
        double bd = Double.MAX_VALUE;
        for (Entity e : enemiesOf(from)) {
            if (inside(v, e) || from.distance(e) <= defend) {
                double dist = from.distance(e);
                if (dist < bd) {
                    best = e;
                    bd = dist;
                }
            }
        }
        return best;
    }

    /** What a monster attacks: the nearest living squad member (pets are ignored so that they cannot be used as decoys forever). */
    private Entity nearestPlayerSide(Monster m) {
        Entity best = null;
        double bd = Double.MAX_VALUE;
        for (Fighter f : squad) {
            if (f.alive) {
                double dist = m.distance(f);
                if (dist < bd) {
                    best = f;
                    bd = dist;
                }
            }
        }
        return best;
    }

    private Entity find(int id) {
        for (Entity e : entities) {
            if (e.id == id) {
                return e;
            }
        }
        return null;
    }

    static boolean inRect(double ox, double oy, double dx, double dy, double length, double width, Entity e) {
        double rx = e.x - ox;
        double ry = e.y - oy;
        double along = rx * dx + ry * dy;
        double across = Math.abs(rx * -dy + ry * dx);
        return along >= -e.radius && along <= length + e.radius && across <= width / 2.0 + e.radius;
    }

    private void moveToward(Entity e, double tx, double ty, double step) {
        double dx = tx - e.x;
        double dy = ty - e.y;
        double dist = Math.hypot(dx, dy);
        if (dist < 1e-6) {
            return;
        }
        double s = Math.min(step, dist);
        e.x = clamp(e.x + dx / dist * s, 0, arenaW);
        e.y = clamp(e.y + dy / dist * s, 0, arenaH);
        e.dirX = dx / dist;
        e.dirY = dy / dist;
    }

    private static void normalizeInto(Entity e, double dx, double dy) {
        double len = Math.hypot(dx, dy);
        if (len > 1e-6) {
            e.dirX = dx / len;
            e.dirY = dy / len;
        }
    }

    private static double clamp(double v, double lo, double hi) {
        return Math.max(lo, Math.min(hi, v));
    }

    private double rule(String key, double fallback) {
        return Json.num(rules, key, fallback);
    }

    private void stun(Entity e, long ms) {
        e.stunUntil = Math.max(e.stunUntil, now + controlMs(e, ms));
        if (e instanceof Fighter f) {
            if (f.cast != null) {
                addEffect(Effect.CAST_END, f, f, 0, f.cast.skill.id());
            }
            f.cast = null;
            breakChannel(f);
            endRegen(f);
        }
        releaseHolds(e);
    }

    /** Control effects on a boss are shortened (balance_config match_rules boss_control_factor / boss_control_max_ms). */
    private long controlMs(Entity e, long ms) {
        if (e instanceof Monster m && m.kind == MonsterKind.BOSS) {
            return Math.min((long) rule("boss_control_max_ms", 3000), Math.round(ms * rule("boss_control_factor", 0.3)));
        }
        return ms;
    }

    private void addEffect(Effect effect, Entity src, Entity target, int durationMs, int skillId) {
        EffectEvent.Builder b = EffectEvent.newBuilder().setEffect(effect).setSourceId(src.id).setTargetId(target.id).setDurationMs(durationMs)
                .setX((float) target.x).setY((float) target.y).setSkillId(skillId);
        if (effect == Effect.CAST_START) {
            b.setDirX((float) src.dirX).setDirY((float) src.dirY);               // the way the skill is aimed: the client turns the character (and draws the beam) that way
        }
        effectBuf.add(b.build());
    }

    // ---------------------------------------------------------------------------------------------------------------- messages

    public EntitySpawn spawnProto(Entity e) {
        EntitySpawn.Builder b = EntitySpawn.newBuilder().setEntityId(e.id).setName(e.name).setX((float) e.x).setY((float) e.y)
                .setHpMax(e.hpMax).setHp(e.hp).setKiMax((long) e.kiMax).setKi((long) e.ki).setRadius((float) e.radius);
        if (e instanceof Fighter f) {
            b.setKind(f.controlled ? EntityKind.PLAYER_CHARACTER : EntityKind.ALLY_AI).setRace(f.race).setCharacterId(f.characterId).setLevel(f.level)
                    .setCostumeId(f.costumeId).setControlled(f.controlled).setPartnerCharacterId(f.partnerCharacterId);
        } else if (e instanceof Dummy dm) {
            b.setKind(EntityKind.TRAINING_DUMMY).setDummyKey(dm.key).setMobTemplateId(dm.mobTemplateId).setCostumeId(dm.costumeId);
        } else if (e instanceof Monster m) {
            b.setKind(m.kind == MonsterKind.BOSS ? EntityKind.BOSS : EntityKind.MINION).setBossId(m.boss.id()).setPartHead(m.boss.head()).setPartBody(m.boss.body())
                    .setPartLeg(m.boss.leg()).setRace(m.boss.race() == null ? "" : m.boss.race()).setPlanet(m.planet).setSummoned(m.summoned);
        } else {
            b.setKind(EntityKind.PET);
            if (e instanceof PetEntity pe) {
                b.setMobTemplateId(pe.mobTemplateId);
            }
        }
        return b.build();
    }

    /** All entities as they are now (for a client that joins or returns). */
    public List<EntitySpawn> snapshotSpawns() {
        List<EntitySpawn> out = new ArrayList<>();
        for (Entity e : entities) {
            if (e.alive) {
                out.add(spawnProto(e));
            }
        }
        return out;
    }

    /** Builds the frame of this tick and clears the event buffers. */
    public MatchFrame buildFrame() {
        MatchFrame.Builder b = MatchFrame.newBuilder().setServerTick(tick).setPaused(paused).setBossesDefeated(bossesDefeated)
                .setElapsedS((int) (now / 1000)).setRevivesLeft(Math.max(0, revivesLeft())).setKillStreak(streak).setAckSeq(lastSeq);
        for (Entity e : entities) {
            b.addStates(EntityState.newBuilder().setEntityId(e.id).setX((float) e.x).setY((float) e.y).setDirX((float) e.dirX).setDirY((float) e.dirY)
                    .setHp(Math.max(0, e.hp)).setHpMax(e.hpMax).setKi((long) e.ki).setKiMax((long) e.kiMax).setAlive(e.alive).setEffects(effectMask(e))
                    .setAtk(Math.round(e.atk)).setArmor(Math.round(e instanceof Fighter ff ? ff.armorStat * (now < ff.buffUntil ? ff.buffArmorMult : 1.0) : 0))
                    .setCritRatePct((float) e.effectiveCritRate(now)).setCritDmgPct((float) e.critDmgPct));
        }
        for (Projectile p : projectiles) {
            b.addProjectiles(ProjectileState.newBuilder().setProjectileId(p.id).setOwnerId(p.owner.id).setX((float) p.x).setY((float) p.y)
                    .setDirX((float) p.dirX).setDirY((float) p.dirY).setRadius((float) p.radius).setSkillId(p.skillId));
        }
        b.addAllDamages(damageBuf).addAllEffects(effectBuf).addAllSpawns(spawnBuf);
        // despawn what died more than a moment ago (the client keeps the corpse for the tick it died)
        for (Iterator<Entity> it = entities.iterator(); it.hasNext(); ) {
            Entity e = it.next();
            if (!e.alive && !(e instanceof Fighter)) {
                b.addDespawns(e.id);
                it.remove();
            }
        }
        damageBuf.clear();
        effectBuf.clear();
        spawnBuf.clear();
        despawnBuf.clear();
        return b.build();
    }

    private int effectMask(Entity e) {
        int m = e.effectMask(now);
        if (e instanceof Monster mo && mo.enraged) {
            m |= 128;
        }
        return m;
    }
}
