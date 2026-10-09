package com.battleball.server.game.match;

import java.util.List;
import java.util.Map;

/** What happened in a match, handed to the settlement (rewards, EXP, drops) when it ends. */
public record MatchOutcome(
        com.battleball.proto.MatchResult result,
        int bossesDefeated,
        int bossCount,
        int durationS,
        long goldEarned,                       // before any loss factor
        Map<Long, Double> expRaw,              // by character id: EXP before the win bonus (overlevel and kill streak already applied)
        List<Kill> kills,
        int revivesUsed,
        long reviveGoldSpent,
        Map<Long, Long> damageDealt,           // by character id, for the anomaly checks
        boolean finalBossDefeated,
        double fastestBossKillS) {

    /** One kill: its class (BOSS, MINI, FINAL, MOB, ELITE) and whether it was a real boss. */
    public record Kill(String classKey, int bossId, boolean realBoss) {
    }
}
