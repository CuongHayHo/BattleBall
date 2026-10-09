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
    private final Fighter controlled;
    private final List<Fighter> squad = new ArrayList<>();
    private final MatchSetup.Training training;
    private final double arenaW;
    private final double arenaH;
    private long lastInputAt;            // training: the session ends after training.idleEndMs without input

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

    /** A visible danger zone (boss ultimate): allies try to step out of it. */
    private record TelegraphArea(boolean rect, double x, double y, double dirX, double dirY, double length, double width, long until) {
    }

    public MatchEngine(MatchSetup setup) {
        this.setup = setup;
        this.d = setup.data();
        this.rules = d.balanceObj("match_rules");
        this.rng = setup.random();
        this.training = setup.training();
        this.arenaW = training != null ? training.arenaW() : rule("arena_w", 1600);
        this.arenaH = training != null ? training.arenaH() : rule("arena_h", 900);
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

    /** Training: an AI character of the squad stands where the client's village puts it (kept inside the arena). */
    public void reportAlly(int entityId, double x, double y, double dx, double dy) {
        lastInputAt = now;
        if (training == null) {
            return;
        }
        for (Fighter f : squad) {
            if (f.id == entityId && !f.controlled && f.alive && f.canMove(now) && f.cast == null) {
                f.x = clamp(x, 0, arenaW);
                f.y = clamp(y, 0, arenaH);
                normalizeInto(f, dx, dy);
            }
        }
    }

    /** The controlled player moved: the position is taken as reported but kept inside the arena. */
    public void reportPosition(int seq, double x, double y, double dx, double dy) {
        lastSeq = Math.max(lastSeq, seq);
        lastInputAt = now;
        Fighter f = controlled;
        if (f == null || !f.alive || !f.canMove(now) || f.cast != null) {
            return;
        }
        f.x = clamp(x, 0, arenaW);
        f.y = clamp(y, 0, arenaH);
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
        f.stunUntil = f.holdUntil = f.sleepUntil = f.chocolateUntil = 0;
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
        f.armorReduction = StatCalculator.armorReduction(d, s.armor());
        f.critRatePct = s.critRatePct();
        f.critDmgPct = s.critDmgPct();
        f.dodgePct = s.dodgePct();
        f.lifestealPct = s.other("LIFESTEAL_PCT");
        f.kiStealPct = s.other("KI_STEAL_PCT");
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

    private void updateFighter(Fighter f, double dt) {
        // regeneration: Ki every second, HP only while Ki is above zero (CONFIRMED rule)
        f.ki = Math.min(f.kiMax, f.ki + f.kiMax * f.kiRegenPctPerSec / 100.0 * dt);
        if (f.ki > 0) {
            heal(f, f.hpMax * f.hpRegenPctPerSec / 100.0 * dt);
        }
        if (now < f.regenUntil) {
            heal(f, f.hpMax * f.regenPctPerSec / 100.0 * dt);
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
        if (f.cast != null) {
            if (!f.canAct(now)) {
                f.cast = null;                       // stun or sleep breaks the cast
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
        if (!f.alive || !f.canAct(now) || f.cast != null) {
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
        if (!startSkill(f, sk, L, slot, target, ndx, ndy, kiCost, allKi)) {
            return false;
        }
        return true;
    }

    private boolean startSkill(Fighter f, StaticData.Skill sk, StaticData.SkillLevel L, int slot, Entity target, double dx, double dy, double kiCost, boolean allKi) {
        Map<String, Object> p = L.params();
        int id = sk.id();
        long cd = L.cooldownMs();
        switch (id) {
            case 0, 2, 4, 9, 17 -> {                                                       // punches, Kaioken, Liên hoàn: instant melee rectangle
                if (id == 9) {
                    long cost = Math.round(f.hpMax * Json.num(p, "selfHpCostPctOfMax", 10) / 100.0);
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
                long ms = (long) Json.num(p, "stunMs", 3000);
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
                pet.expiresAt = now + (long) Json.num(p, "lifetimeMs", 120000);
                pet.damagePct = Json.num(p, "petDamagePctOfAttack", 30);
                pet.atk = f.atk;
                pet.critRatePct = 0;
                pet.armorReduction = 0;
                entities.add(pet);
                spawnBuf.add(spawnProto(pet));
                return true;
            }
            case 13 -> {                                                                   // Biến hình: more HP for a while (Biến hình only adds HP, CONFIRMED)
                if (f.hpBonusAmount > 0) {
                    return false;
                }
                pay(f, slot, cd, kiCost, allKi);
                long add = Math.round(f.hpMax * Json.num(p, "hpBonusPct", 40) / 100.0);
                f.hpBonusAmount = add;
                f.hpMax += add;
                f.hp += add;
                f.hpBonusUntil = now + (long) Json.num(p, "durationMs", 60000);
                addEffect(Effect.BUFF, f, f, (int) Json.num(p, "durationMs", 60000), id);
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
            case 22 -> {                                                                   // Thôi miên: everything near the target falls asleep
                long ms = (long) Json.num(p, "sleepMs", 5000);
                double radius = L.radiusPx() == null ? 200 : L.radiusPx();
                for (Entity e : enemiesOf(f)) {
                    if (e == t || t.distance(e) <= radius) {
                        long eff = controlMs(e, ms);
                        e.sleepUntil = now + eff;
                        addEffect(Effect.SLEEP, f, e, (int) eff, id);
                    }
                }
            }
            case 23 -> {                                                                   // Trói: the target cannot move
                long ms = (long) Json.num(p, "holdMs", 5000);
                long eff = controlMs(t, ms);
                t.holdUntil = now + eff;
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
            case 24, 25 -> {
                double length = L.lengthPx() == null ? 200 : L.lengthPx();
                double width = L.widthPx() == null ? 60 : L.widthPx();
                double base = f.atk * pct(L) / 100.0;
                for (Entity e : enemiesOf(f)) {
                    if (inRect(f.x, f.y, c.dirX, c.dirY, length, width, e)) {
                        hit(f, e, base, id, false, false, false);
                    }
                }
            }
            case 11 -> {
                double length = L.lengthPx() == null ? 2000 : L.lengthPx();
                double width = L.widthPx() == null ? 48 : L.widthPx();
                double base = c.kiAtCast * (L.damagePctMaxKi() == null ? 70 : L.damagePctMaxKi()) / 100.0;
                for (Entity e : enemiesOf(f)) {
                    if (inRect(f.x, f.y, c.dirX, c.dirY, length, width, e)) {
                        hit(f, e, base, id, false, false, false);
                    }
                }
            }
            case 26 -> {
                double radius = L.radiusPx() == null ? 500 : L.radiusPx();
                long ms = (long) Json.num(p, "trapMs", 4000);
                for (Entity e : enemiesOf(f)) {
                    double ax = e.x - f.x;
                    double ay = e.y - f.y;
                    double dist = Math.hypot(ax, ay);
                    boolean front = dist < 1e-6 || (ax * c.dirX + ay * c.dirY) / dist >= -0.01;
                    if (dist <= radius + e.radius && front) {
                        long eff = controlMs(e, ms);
                        e.holdUntil = now + eff;
                        e.stunUntil = Math.max(e.stunUntil, now + eff);
                        addEffect(Effect.HOLD, f, e, (int) eff, id);
                    }
                }
            }
            case 10 -> {                                                                   // 10% of the target's HP plus 10 x the caster's attack
                double radius = L.radiusPx() == null ? 380 : L.radiusPx();
                for (Entity e : enemiesOf(f)) {
                    if (f.distance(e) <= radius + e.radius) {
                        hit(f, e, e.hp * 0.10 + 10 * f.atk, id, false, false, false);
                    }
                }
            }
            case 14 -> {                                                                   // Tự phát nổ: damage = the caster's current HP, ignores armor, halved against bosses; the caster dies
                double radius = L.radiusPx() == null ? 430 : L.radiusPx();
                double divisor = Json.num(p, "vsBossDamageDivisor", 2);
                double dmg = f.hp;
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
        double dmg = base;
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
        if (victim instanceof Fighter f) {
            f.cast = null;
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
        f.stunUntil = f.holdUntil = f.sleepUntil = f.chocolateUntil = 0;
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
            // wipe the remaining minions of this fight and heal the squad a little before the next one
            for (Entity e : new ArrayList<>(entities)) {
                if (e instanceof Monster x && x.alive && x.kind == MonsterKind.MINION) {
                    x.alive = false;
                    x.hp = 0;
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
            int cap = (int) d.balanceNum("minion_cap_per_match", 20);
            double expectedWeight = Math.max(1, cap) * averagePoolWeight(split);
            goldEarned += Math.round(winGold * Json.num(split, "mob_share", 0.4) * weight / expectedWeight);
        }
        if (killer instanceof Fighter f) {
            f.kills++;
        }
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
        Entity target = nearestEnemy(f, 1e9);               // each AI character fights what is nearest to it (training: the client walks it to the nearest dummy in view)
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
        boolean bossAlive = training != null || (currentBoss != null && currentBoss.alive);     // a training dummy counts as the boss of the fight
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
            case 13 -> bossAlive && f.hpBonusAmount == 0;
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
        if (currentBoss == null && encounterIdx < setup.encounters().size() && now >= nextBossAt) {
            spawnBoss(setup.encounters().get(encounterIdx++));
        }
        if (currentBoss != null && currentBoss.alive && !setup.minionPool().isEmpty()) {
            int cap = (int) d.balanceNum("minion_cap_per_match", 20);
            long alive = entities.stream().filter(e -> e instanceof Monster m && m.alive && m.kind == MonsterKind.MINION).count();
            if (now >= nextWaveAt && minionsSpawned < cap && alive < rule("minion_alive_cap", 6)) {
                nextWaveAt = now + (long) (rule("minion_wave_every_s", 8) * 1000);
                for (int i = 0; i < (int) rule("minion_wave_size", 2) && minionsSpawned < cap; i++) {
                    spawnMinion(setup.minionPool().get(minionPoolCursor++ % setup.minionPool().size()));
                    minionsSpawned++;
                }
            }
        }
    }

    private void spawnBoss(MatchSetup.Encounter enc) {
        StaticData.MonsterStat ms = d.monsterStat(enc.classKey(), setup.dungeon().tier());
        Monster m = new Monster(nextId++, enc.boss().name(), MonsterKind.BOSS, enc.boss(), enc.classKey(), enc.role(), rule("boss_radius", 34));
        applyMonsterStats(m, ms, enc.boss());
        m.x = arenaW - 380;
        m.y = arenaH / 2;
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
        currentBoss = m;
        bossSpawnedAt = now;
        nextWaveAt = now + (long) (rule("minion_wave_every_s", 8) * 1000);
    }

    private void spawnMinion(MatchSetup.Encounter enc) {
        StaticData.MonsterStat ms = d.monsterStat(enc.classKey(), setup.dungeon().tier());
        Monster m = new Monster(nextId++, enc.boss().name(), MonsterKind.MINION, enc.boss(), enc.classKey(), "MINION", rule("minion_radius", 18));
        applyMonsterStats(m, ms, enc.boss());
        double ang = rng.nextDouble() * Math.PI * 2;
        double cx = currentBoss != null ? currentBoss.x : arenaW - 380;
        double cy = currentBoss != null ? currentBoss.y : arenaH / 2;
        m.x = clamp(cx + Math.cos(ang) * 120, 0, arenaW);
        m.y = clamp(cy + Math.sin(ang) * 120, 0, arenaH);
        m.homeX = m.x;
        m.homeY = m.y;
        entities.add(m);
        spawnBuf.add(spawnProto(m));
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

    private void minionAct(Monster m, double dt) {
        Entity t = nearestPlayerSide(m);
        if (t == null) {
            return;
        }
        double range = rule("minion_melee_range", 40);
        if (m.distance(t) > range + t.radius) {
            if (m.canMove(now)) {
                moveToward(m, t.x, t.y, rule("minion_speed", 170) * dt);
            }
        } else if (now >= m.nextPunchAt) {
            m.nextPunchAt = now + (long) (rule("minion_hit_interval_s", 1.2) * 1000);
            normalizeInto(m, t.x - m.x, t.y - m.y);
            hit(m, t, m.atkPerHit, 0, false, false, false);
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

    /** One boss action: the first ready entry of the priority list that makes sense now (docs 7.5). Returns false when nothing was possible. */
    private boolean performBossAction(Monster m, Entity target, double meleeRange) {
        for (Map<String, Object> a : bossActions()) {
            String name = (String) a.get("name");
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
                    bossUltimate(m, target, base);
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
                    m.actionReadyAt.put(name, now + (long) (cd * 1000));
                    for (Fighter f : squad) {
                        if (f.alive && m.distance(f) <= radius + f.radius) {
                            hit(m, f, base, 6, false, false, false);
                            stun(f, (long) (rule("boss_stun_s", 2) * 1000));
                            addEffect(Effect.STUN, m, f, (int) (rule("boss_stun_s", 2) * 1000), 6);
                        }
                    }
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
                    hit(m, top, base, 0, false, false, true);          // locks on the unit with the most HP; cannot be dodged
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
                            rule("boss_beam_width", 60) / 2, base, 1, false, false));
                    return true;
                }
                default -> {                                                                // punch
                    if (m.distance(target) > meleeRange + target.radius) {
                        continue;
                    }
                    normalizeInto(m, target.x - m.x, target.y - m.y);
                    hit(m, target, base, 0, false, false, false);
                    return true;
                }
            }
        }
        return false;
    }

    private void bossUltimate(Monster m, Entity target, double base) {
        normalizeInto(m, target.x - m.x, target.y - m.y);
        long ms = (long) (rule("boss_ultimate_telegraph_s", 2) * 1000);
        double length = rule("boss_ultimate_length", 500);
        double width = rule("boss_ultimate_width", 160);
        double ox = m.x;
        double oy = m.y;
        double dx = m.dirX;
        double dy = m.dirY;
        telegraphs.add(new TelegraphArea(true, ox, oy, dx, dy, length, width, now + ms + 200));
        effectBuf.add(EffectEvent.newBuilder().setEffect(Effect.TELEGRAPH_RECT).setSourceId(m.id).setDurationMs((int) ms).setX((float) ox).setY((float) oy)
                .setDirX((float) dx).setDirY((float) dy).setLength((float) length).setWidth((float) width).build());
        delayed.add(new Delayed(now + ms, () -> {
            for (Fighter f : squad) {
                if (f.alive && inRect(ox, oy, dx, dy, length, width, f)) {
                    hit(m, f, base, 24, false, false, false);
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
            f.cast = null;
        }
    }

    /** Control effects on a boss are shortened (balance_config match_rules boss_control_factor / boss_control_max_ms). */
    private long controlMs(Entity e, long ms) {
        if (e instanceof Monster m && m.kind == MonsterKind.BOSS) {
            return Math.min((long) rule("boss_control_max_ms", 3000), Math.round(ms * rule("boss_control_factor", 0.3)));
        }
        return ms;
    }

    private void addEffect(Effect effect, Entity src, Entity target, int durationMs, int skillId) {
        effectBuf.add(EffectEvent.newBuilder().setEffect(effect).setSourceId(src.id).setTargetId(target.id).setDurationMs(durationMs)
                .setX((float) target.x).setY((float) target.y).setSkillId(skillId).build());
    }

    // ---------------------------------------------------------------------------------------------------------------- messages

    public EntitySpawn spawnProto(Entity e) {
        EntitySpawn.Builder b = EntitySpawn.newBuilder().setEntityId(e.id).setName(e.name).setX((float) e.x).setY((float) e.y)
                .setHpMax(e.hpMax).setHp(e.hp).setKiMax((long) e.kiMax).setKi((long) e.ki).setRadius((float) e.radius);
        if (e instanceof Fighter f) {
            b.setKind(f.controlled ? EntityKind.PLAYER_CHARACTER : EntityKind.ALLY_AI).setRace(f.race).setCharacterId(f.characterId).setLevel(f.level)
                    .setCostumeId(f.costumeId).setControlled(f.controlled);
        } else if (e instanceof Dummy dm) {
            b.setKind(EntityKind.TRAINING_DUMMY).setDummyKey(dm.key).setMobTemplateId(dm.mobTemplateId).setCostumeId(dm.costumeId);
        } else if (e instanceof Monster m) {
            b.setKind(m.kind == MonsterKind.BOSS ? EntityKind.BOSS : EntityKind.MINION).setBossId(m.boss.id()).setPartHead(m.boss.head()).setPartBody(m.boss.body())
                    .setPartLeg(m.boss.leg()).setRace(m.boss.race() == null ? "" : m.boss.race());
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
                    .setHp(Math.max(0, e.hp)).setHpMax(e.hpMax).setKi((long) e.ki).setKiMax((long) e.kiMax).setAlive(e.alive).setEffects(effectMask(e)));
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
