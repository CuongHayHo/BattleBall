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
        /** The Trói of Xayda: who holds this entity (the rope breaks when that fighter moves, acts or is stopped). 0 = nobody. */
        public int holdById;
        /** Ma phong ba (Nro EffectSkillService.setIsBinh): sucked into the jar from {@code jarFrom} to {@code jarGatherEnd}, a jar until {@code jarUntil}. */
        public long jarFrom;
        public long jarGatherEnd;
        public long jarUntil;
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
            return alive && now >= stunUntil && now >= sleepUntil && now >= chocolateUntil && now >= jarUntil;
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
            if (now >= jarFrom && now < jarGatherEnd) {
                m |= 1024;                       // being sucked into the jar
            } else if (now >= jarGatherEnd && now < jarUntil) {
                m |= 512;                        // a jar
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

    /**
     * The part of an ultimate after its gong (Nro NewSkill, updateSkillSpecial): for {@code endsAt} the fighter stays where it is (the client paints the skill and
     * takes no input) and every {@code tickMs} the skill hits everything in its rectangle, all of it from the point where the skill was started.
     */
    public static final class Channel {
        public final StaticData.Skill skill;
        public final StaticData.SkillLevel level;
        public final long endsAt;
        public long nextTickAt;
        public final long tickMs;
        public final double originX;
        public final double originY;
        public final double dirX;
        public final double dirY;
        /** Against small monsters of a dungeon the beam turns around the caster (docs 15.25): radians per millisecond (0 = a fixed beam), the tick it is at and where it pointed at the last tick. */
        public double spinRadPerMs;
        public int tickIndex;
        public double lastAngle;
        /** The reach of a spinning beam (a fixed beam uses the length of the skill level). */
        public double spinLength;

        public Channel(StaticData.Skill skill, StaticData.SkillLevel level, long startsAt, long endsAt, long tickMs, double originX, double originY, double dirX, double dirY) {
            this.skill = skill;
            this.level = level;
            this.endsAt = endsAt;
            this.nextTickAt = startsAt;
            this.tickMs = tickMs;
            this.originX = originX;
            this.originY = originY;
            this.dirX = dirX;
            this.dirY = dirY;
        }
    }

    public static final class Fighter extends Entity {
        public final long characterId;
        public final String race;
        public final int level;
        public boolean controlled;                       // changes when the player takes over another character (MatchEngine.switchControl)
        public final boolean fusion;
        public final long partnerCharacterId;
        public final Map<Integer, StaticData.Skill> skillBySlot = new HashMap<>();
        public final Map<Integer, Integer> skillLevelBySlot = new HashMap<>();
        public final Map<Integer, Long> cooldownUntil = new HashMap<>();
        public final int costumeId;
        public Cast cast;
        public Channel channel;
        /** One skill at a time: until this moment the fighter is still doing the last skill (its animation) and starts no other (the Nro client refuses while the paint plays). */
        public long actionUntil;
        public long regenUntil;
        public double regenPctPerSec;
        /** Where the fighter was when the regeneration (Tái tạo năng lượng) started; moving away from it ends the skill (Nro PlayerService.playerMove). */
        public double regenAtX;
        public double regenAtY;
        public long hpBonusUntil;
        public long hpBonusAmount;
        // Biến hình (Nro EffectSkill isMonkey): more max HP, more attack, a critical rate of 110; own fields so that Huýt sáo can run at the same time
        public long monkeyUntil;
        public long monkeyHpAdd;
        public double monkeyAtkAdd;
        public double monkeyCritBefore;
        public boolean nextHitCrit;
        public long nextHitCritUntil;
        // pet effect
        public StaticData.PetEffect pet;
        public long nextPetAt;
        public double hpRegenPctPerSec;
        public double kiRegenPctPerSec;
        // bookkeeping for the result
        public long damageDealt;
        public int kills;

        /** Display values of the armor stat (the menu shows them live) and the base for the buffs of the pet effect. */
        public double armorStat;
        /** The figures the fighter has now without any temporary effect (a refresh while a session runs moves everything by the difference to these). */
        public com.battleball.server.game.StatCalculator.CharacterStats base;

        /**
         * What the GEN sets worn by this fighter add to one skill (docs 15.21): {@code type} is SKILL_DMG_PCT, SKILL_COOLDOWN_PCT, SKILL_DURATION_MULT, SKILL_HP_COST_DELTA,
         * SKILL_RADIUS_PX, SKILL_DMG_HPMAX_PCT or MINION_PERSIST. 0 when no set gives it. A fusion adds the numbers of both characters (its base is their sum).
         */
        public double gen(String type, int skillId) {
            return base == null ? 0 : base.other(type + ":" + skillId);
        }

        @Override
        public int effectMask(long now) {
            int m = super.effectMask(now);
            if (now < regenUntil) {
                m |= 32;
            }
            if (cast != null || channel != null) {
                m |= 256;                        // busy: gong, charge or the beam of an ultimate (the client takes no input)
            }
            if (now < monkeyUntil) {
                m |= 2048;
            }
            return m;
        }

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
        public StaticData.Boss boss;           // changes when an evolving boss turns into its next form
        public String classKey;                // BOSS, MINI, FINAL, MOB, ELITE
        public String role;                    // NORMAL, MINI, FINAL, MINION
        /** An evolving boss (docs 15.30): its forms in order, the one it is in, and until when it charges (cannot be hurt) before the next one. */
        public java.util.List<StaticData.Boss> forms;
        public int formIdx;
        public long evolveUntil;
        /** Huýt sáo of a boss: its blows are stronger until this moment; Tái tạo năng lượng: it heals and stands still until this moment. */
        public long rageUntil;
        public double rageMult = 1.0;
        public long regenUntil;
        /** The kit of moves in balance_config boss_planet_kits when it is not the planet's own ("APE"); null = the planet's. */
        public String kitKey;
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
        /** A dungeon monster: the planet whose skills it uses (EARTH, NAMEC or XAYDA); a boss draws it at random for the match. */
        public String planet = "EARTH";
        /** Called by a boss (docs 15.25): it pays no gold and drops nothing; the waves are what pays. */
        public boolean summoned;
        public long shootReadyAt;
        /** A small monster that is aiming or has just shot stands still until this moment. */
        public long actionUntil;

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
