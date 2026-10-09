package com.battleball.server.data;

import java.util.List;
import java.util.Map;

/**
 * An immutable snapshot of all static game data (everything the admin page edits). The server swaps the whole snapshot atomically when
 * {@code static_data_version} changes, so a match never sees half-updated rules.
 */
public record StaticData(
        Map<String, Integer> versions,
        Map<String, Object> balance,
        Map<String, StatType> stats,
        Map<String, SkinKind> skinKinds,
        Map<Integer, Skin> skins,
        Map<Integer, Skill> skills,
        Map<Integer, ItemTemplate> items,
        Map<Integer, Material> materials,
        Map<Integer, SupportItem> supportItems,
        List<BookPoolEntry> bookPool,
        List<Recipe> recipes,
        List<DropRule> dropRules,
        Map<String, Double> dungeonDropMult,
        List<GemRule> gemRules,
        List<Achievement> achievements,
        Map<String, List<Integer>> defaultSkinsByRace,
        Map<String, Integer> equippedDefaultSkinByRace,
        Map<Integer, Dungeon> dungeons,
        Map<Integer, Boss> bosses,
        Map<String, MonsterStat> monsterStats) {

    public record StatType(String key, String name, boolean enabled, String unit, Double suggestedMax) {
    }

    public record SkinKind(String key, boolean sellable, boolean playerWearable, boolean requiresFusionMain, boolean enabled) {
    }

    public record Skin(int id, String name, int genderId, String kindKey, String rarity, Integer gemPrice, String fusionRace,
                       boolean aiCanWear, boolean enabled, Map<String, Double> stats) {
    }

    public record SkillLevel(int level, Double damagePctAtk, Double damagePctMaxKi, Double kiCostPct, int cooldownMs, long goldCost,
                             Integer lengthPx, Integer widthPx, Integer radiusPx, Integer projectileSpeed, Map<String, Object> params) {
    }

    public record Skill(int id, String race, String name, String kind, String kiMode, String hitShape, int slot, int maxLevel,
                        boolean enabled, Map<Integer, SkillLevel> levels, int iconId, String damInfo) {     // iconId: Nro icon of the skill button; damInfo: the Nro description
        public SkillLevel level(int lv) {
            return levels.get(lv);
        }
    }

    public record ItemTemplate(int id, String key, String slot, String name, int tier, String baseStatKey, double baseValue, int piecesNeeded,
                               int minLevel, boolean enabled) {
    }

    public record Material(int id, String key, String kind, String name, Integer itemTemplateId, String buyCurrency, Double priceWins,
                           Integer priceFixed, boolean enabled) {
    }

    public record PetEffect(String statKey, double value, int intervalS, int durationS) {
    }

    public record TrainArmor(int level, double atkPenaltyPct, double atkBonusPct, int pointsPerMatch, int maxPoints) {
    }

    public record SupportItem(int id, String kind, String race, String name, String obtain, Integer gemPrice, boolean enabled,
                              Map<String, Double> stats, PetEffect pet, TrainArmor train, int bookLines) {
    }

    public record BookPoolEntry(String statKey, double min, double max, int weight) {
    }

    public record RecipeInput(int materialId, int quantity, double loseOnFailPct) {
    }

    public record Recipe(int id, String name, String type, Integer inputSupportId, int resultSupportId, double successPct, Integer boostMaterialId,
                         Double boostSuccessPct, double extraLineChancePct, int extraLinesMin, int extraLinesMax, boolean enabled,
                         List<RecipeInput> inputs) {
    }

    public record DropRule(String sourceClass, String tier, int materialId, Integer dungeonId, double ratePct, int qtyMin, int qtyMax) {
    }

    public record GemRule(String type, Integer dungeonId, int gems, Integer dailyCap, boolean enabled) {
    }

    public record Achievement(int id, String name, String race, String condition, int threshold, Integer rewardCostumeId, Integer rewardGems) {
    }

    public record DungeonBoss(int bossId, String role, String mobTier, int sortOrder) {
    }

    public record Dungeon(int id, String name, String tier, int bossesPerMatch, Integer timeLimitS, String groupKey, boolean enabled,
                          List<DungeonBoss> bosses) {
    }

    public record Boss(int id, String groupKey, String name, String race, int head, int body, int leg, double hpMult, double atkMult, boolean enabled) {
    }

    public record MonsterStat(String classKey, String tier, double ttkSeconds, long hp, double atkMin, double atkMax, double armorMin,
                              double armorMax, Double actionGapS) {
    }

    // ---- balance helpers ----
    public double balanceNum(String key, double fallback) {
        Object v = balance.get(key);
        return v instanceof Double d ? d : fallback;
    }

    public Map<String, Object> balanceObj(String key) {
        return Json.obj(balance, key);
    }

    /**
     * A bonus line as the player reads it, from the stat's Vietnamese name: the first "%" of the name is replaced by the signed value ("Sức đánh %" becomes
     * "Sức đánh +100%"), a flat stat gets the value at the end, the "(NRO: ...)" remark of the admin tables is dropped.
     */
    public String statText(String key, double value) {
        StatType t = stats.get(key);
        if (t == null) {
            return key + " +" + number(value);
        }
        String name = t.name().replaceAll("\\s*\\(NRO[^)]*\\)", "").trim();
        String v = "+" + number(value);
        if (name.contains("%")) {
            return name.replaceFirst("%", java.util.regex.Matcher.quoteReplacement(v + "%"));
        }
        return name + " " + v + ("PCT".equals(t.unit()) ? "%" : "");
    }

    private static String number(double v) {
        String s = Math.abs(v - Math.rint(v)) < 1e-9 ? Long.toString(Math.round(v)) : String.format(java.util.Locale.ROOT, "%.1f", v);
        return s.replace('.', ',');
    }

    public boolean statEnabled(String key) {
        StatType t = stats.get(key);
        return t != null && t.enabled();
    }

    public MonsterStat monsterStat(String classKey, String tier) {
        return monsterStats.get(classKey + ":" + tier);
    }

    public double dropMult(int dungeonId, int materialId) {
        return dungeonDropMult.getOrDefault(dungeonId + ":" + materialId, 1.0);
    }
}
