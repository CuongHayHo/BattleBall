package com.battleball.server.game.match;

import com.battleball.server.data.StaticData;
import com.battleball.server.game.StatCalculator;

import java.util.List;
import java.util.Map;
import java.util.random.RandomGenerator;

/** Everything the engine needs to run one match, fixed when the match starts (the static data snapshot stays the same for the whole match). */
public record MatchSetup(
        long runId,
        StaticData data,
        StaticData.Dungeon dungeon,
        List<SquadMember> squad,                // the controlled character first
        List<Encounter> encounters,             // real bosses in fighting order (the final boss last)
        List<Encounter> minionPool,             // small mobs that swarm during fights (may be empty)
        long goldAvailable,                     // gold of the controlled character at the start (for revives)
        RandomGenerator random,
        Training training) {                    // null for a real match; a training session of the village otherwise (dungeon is null then)

    public MatchSetup(long runId, StaticData data, StaticData.Dungeon dungeon, List<SquadMember> squad, List<Encounter> encounters, List<Encounter> minionPool,
                      long goldAvailable, RandomGenerator random) {
        this(runId, data, dungeon, squad, encounters, minionPool, goldAvailable, random, null);
    }

    /**
     * A training session of the starter village (owner decision 2026-10-08): the squad against dummies that never fight back and refill their HP when it runs
     * out; no bosses, no time limit, no reward. The client simulates the village (side view with gravity) and reports where all three characters stand.
     */
    public record Training(double arenaW, double arenaH, List<Dummy> dummies, long reviveAfterMs, long idleEndMs) {
    }

    /** A training dummy where the client draws it (balance_config training.dummies plus the spot of the client's map). */
    public record Dummy(String key, String name, long hp, int mobTemplateId, int costumeId, double radius, double armorPct, double x, double y) {
    }

    /** One member of the squad. In a fusion the main character carries the summed stats and the partner is not a separate fighter. */
    public record SquadMember(long characterId, String name, String race, int level, boolean controlled, boolean fusedMain, long partnerCharacterId,
                              StatCalculator.CharacterStats stats, Map<Integer, Integer> skills, int costumeId, StaticData.PetEffect pet) {
    }

    public record Encounter(StaticData.Boss boss, String role, String mobTier) {
        /** BOSS, MINI, FINAL, MOB or ELITE: the class used for stats and drops. */
        public String classKey() {
            return switch (role) {
                case "NORMAL" -> "BOSS";
                case "MINI" -> "MINI";
                case "FINAL" -> "FINAL";
                default -> mobTier == null ? "MOB" : mobTier;
            };
        }
    }
}
