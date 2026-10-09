package com.battleball.server.game.match;

import com.battleball.server.data.StaticData;

import java.util.HashMap;
import java.util.Map;

/** The things that exist in a match. All times are simulation milliseconds (tick x 50). */
public final class Entities {
    private Entities() {
    }

    public enum Team { PLAYER, ENEMY }

    public abstract static class Entity {
        public final int id;
        public final Team team;
        public String name;
        public double x;
        public double y;
        public double dirX = 1;
        public double dirY = 0;
        public double radius;
        public long hp;
        public long hpMax;
        public double ki;
        public double kiMax;
        public boolean alive = true;
        // crowd control and shield (0 = not active)
        public long stunUntil;
        public long holdUntil;
        public long sleepUntil;
        public long chocolateUntil;
        public long shieldUntil;
        // combat numbers
        public double atk;                     // base attack (a hit multiplies it by the skill %)
        public double armorReduction;          // 0..0.8 share of damage removed
        public double critRatePct;
        public double critDmgPct;
        public double dodgePct;
        public double lifestealPct;
        public double kiStealPct;
        // temporary stat buffs (pet effect, buffs)
        public long buffUntil;
        public double buffCritRatePct;
        public double buffArmorMult = 1.0;

        protected Entity(int id, Team team, String name, double radius) {
            this.id = id;
            this.team = team;
            this.name = name;
            this.radius = radius;
        }

        public boolean canAct(long now) {
            return alive && now >= stunUntil && now >= sleepUntil && now >= chocolateUntil;
        }

        public boolean canMove(long now) {
            return canAct(now) && now >= holdUntil;
        }

        public boolean shielded(long now) {
            return now < shieldUntil;
        }

        public double distance(Entity o) {
            return Math.hypot(o.x - x, o.y - y);
        }

        public double effectiveCritRate(long now) {
            return Math.min(100.0, critRatePct + (now < buffUntil ? buffCritRatePct : 0));
        }

        public int effectMask(long now) {
            int m = 0;
            if (now < stunUntil) {
                m |= 1;
            }
            if (now < holdUntil) {
                m |= 2;
            }
            if (now < sleepUntil) {
                m |= 4;
            }
            if (now < shieldUntil) {
                m |= 8;
            }
            if (now < chocolateUntil) {
                m |= 16;
            }
            if (now < buffUntil) {
                m |= 64;
            }
            return m;
        }
    }

    /** A cast in progress (gong, charge). The fighter can do nothing else until it ends; stun or sleep cancels it. */
    public static final class Cast {
        public final StaticData.Skill skill;
        public final StaticData.SkillLevel level;
        public final long endsAt;
        public final int targetId;
        public final double dirX;
        public final double dirY;
        public final double kiAtCast;

        public Cast(StaticData.Skill skill, StaticData.SkillLevel level, long endsAt, int targetId, double dirX, double dirY, double kiAtCast) {
            this.skill = skill;
            this.level = level;
            this.endsAt = endsAt;
            this.targetId = targetId;
            this.dirX = dirX;
            this.dirY = dirY;
            this.kiAtCast = kiAtCast;
        }
    }

    public static final class Fighter extends Entity {
        public final long characterId;
        public final String race;
        public final int level;
        public final boolean controlled;
        public final boolean fusion;
        public final long partnerCharacterId;
        public final Map<Integer, StaticData.Skill> skillBySlot = new HashMap<>();
        public final Map<Integer, Integer> skillLevelBySlot = new HashMap<>();
        public final Map<Integer, Long> cooldownUntil = new HashMap<>();
        public final int costumeId;
        public Cast cast;
        public long regenUntil;
        public double regenPctPerSec;
        public long hpBonusUntil;
        public long hpBonusAmount;
        public boolean nextHitCrit;
        public long nextHitCritUntil;
        // pet effect
        public StaticData.PetEffect pet;
        public long nextPetAt;
        public final double hpRegenPctPerSec;
        public final double kiRegenPctPerSec;
        // bookkeeping for the result
        public long damageDealt;
        public int kills;

        public Fighter(int id, String name, long characterId, String race, int level, boolean controlled, boolean fusion, long partnerCharacterId,
                       int costumeId, double radius, double hpRegenPctPerSec, double kiRegenPctPerSec) {
            super(id, Team.PLAYER, name, radius);
            this.characterId = characterId;
            this.race = race;
            this.level = level;
            this.controlled = controlled;
            this.fusion = fusion;
            this.partnerCharacterId = partnerCharacterId;
            this.costumeId = costumeId;
            this.hpRegenPctPerSec = hpRegenPctPerSec;
            this.kiRegenPctPerSec = kiRegenPctPerSec;
        }
    }

    public enum MonsterKind { BOSS, MINION }

    public static final class Monster extends Entity {
        public final MonsterKind kind;
        public final StaticData.Boss boss;
        public final String classKey;          // BOSS, MINI, FINAL, MOB, ELITE
        public final String role;              // NORMAL, MINI, FINAL, MINION
        public double homeX;
        public double homeY;
        public double atkPerHit;
        public long nextActionAt;
        public long nextPunchAt;
        public double gapS;
        public boolean enraged;
        public boolean returning;
        public double leashOvershoot;
        public final Map<String, Long> actionReadyAt = new HashMap<>();
        public Integer lockedTarget;

        public Monster(int id, String name, MonsterKind kind, StaticData.Boss boss, String classKey, String role, double radius) {
            super(id, Team.ENEMY, name, radius);
            this.kind = kind;
            this.boss = boss;
            this.classKey = classKey;
            this.role = role;
        }
    }

    /** A training dummy (village training): it never acts and gets its HP back when it runs out. */
    public static final class Dummy extends Entity {
        public final String key;
        public final int mobTemplateId;
        public final int costumeId;

        public Dummy(int id, String key, String name, int mobTemplateId, int costumeId, double radius) {
            super(id, Team.ENEMY, name, radius);
            this.key = key;
            this.mobTemplateId = mobTemplateId;
            this.costumeId = costumeId;
        }
    }

    public static final class PetEntity extends Entity {
        public final Fighter owner;
        public long expiresAt;
        public double damagePct;
        public long nextHitAt;
        public int mobTemplateId = -1;       // the Nro mob the egg hatches into (drawn by the client): by level of Đẻ trứng

        public PetEntity(int id, Fighter owner) {
            super(id, Team.PLAYER, "Trứng của " + owner.name, 14);
            this.owner = owner;
        }
    }

    public static final class Projectile {
        public final int id;
        public final Entity owner;
        public final Team team;
        public double x;
        public double y;
        public final double dirX;
        public final double dirY;
        public final double speed;
        public double remaining;
        public final double radius;
        public final double baseDamage;
        public final int skillId;
        public final boolean ignoreArmor;
        public final boolean forceCrit;
        public final java.util.Set<Integer> alreadyHit = new java.util.HashSet<>();

        public Projectile(int id, Entity owner, double x, double y, double dirX, double dirY, double speed, double range, double radius,
                          double baseDamage, int skillId, boolean ignoreArmor, boolean forceCrit) {
            this.id = id;
            this.owner = owner;
            this.team = owner.team;
            this.x = x;
            this.y = y;
            this.dirX = dirX;
            this.dirY = dirY;
            this.speed = speed;
            this.remaining = range;
            this.radius = radius;
            this.baseDamage = baseDamage;
            this.skillId = skillId;
            this.ignoreArmor = ignoreArmor;
            this.forceCrit = forceCrit;
        }
    }

    /** Something that happens later: a telegraphed boss attack, a delayed explosion. */
    public static final class Delayed {
        public final long at;
        public final Runnable action;
        public final Entity owner;

        public Delayed(long at, Runnable action, Entity owner) {
            this.at = at;
            this.action = action;
            this.owner = owner;
        }
    }
}
