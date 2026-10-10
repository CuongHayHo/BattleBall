package com.battleball.server.data;

import com.battleball.server.db.Database;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/** Reads every static table into a {@link StaticData} snapshot (one consistent read inside one transaction). */
public final class StaticDataLoader {
    private final Database db;

    public StaticDataLoader(Database db) {
        this.db = db;
    }

    @FunctionalInterface
    private interface RowMapper<T> {
        T map(ResultSet rs) throws SQLException;
    }

    public StaticData load() throws SQLException {
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            c.setTransactionIsolation(Connection.TRANSACTION_REPEATABLE_READ);
            try {
                return read(c);
            } finally {
                c.rollback();
            }
        }
    }

    /** The current version of every data domain (cheap; used to notice that the admin page changed something). */
    public Map<String, Integer> versions() throws SQLException {
        try (Connection c = db.connection()) {
            return readVersions(c);
        }
    }

    private static Map<String, Integer> readVersions(Connection c) throws SQLException {
        Map<String, Integer> v = new TreeMap<>();
        for (Object[] r : rows(c, "SELECT domain, version FROM static_data_version", rs -> new Object[]{rs.getString(1), rs.getInt(2)})) {
            v.put((String) r[0], (Integer) r[1]);
        }
        return v;
    }

    private StaticData read(Connection c) throws SQLException {
        Map<String, Integer> versions = readVersions(c);

        Map<String, Object> balance = new LinkedHashMap<>();
        for (Object[] r : rows(c, "SELECT config_key, value_json FROM balance_config", rs -> new Object[]{rs.getString(1), rs.getString(2)})) {
            balance.put((String) r[0], Json.parse((String) r[1]));
        }

        Map<String, StaticData.StatType> stats = new LinkedHashMap<>();
        for (var s : rows(c, "SELECT stat_key, name_vi, enabled, unit, suggested_max FROM stat_type ORDER BY sort_order",
                rs -> new StaticData.StatType(rs.getString(1), rs.getString(2), rs.getBoolean(3), rs.getString(4), (Double) nullableDouble(rs, 5)))) {
            stats.put(s.key(), s);
        }

        Map<String, StaticData.SkinKind> kinds = new LinkedHashMap<>();
        for (var k : rows(c, "SELECT kind_key, sellable, player_wearable, requires_fusion_main, enabled FROM skin_kind",
                rs -> new StaticData.SkinKind(rs.getString(1), rs.getBoolean(2), rs.getBoolean(3), rs.getBoolean(4), rs.getBoolean(5)))) {
            kinds.put(k.key(), k);
        }

        Map<Integer, Map<String, Double>> skinStats = new HashMap<>();
        for (Object[] r : rows(c, "SELECT costume_id, stat_key, `value` FROM skin_stat", rs -> new Object[]{rs.getInt(1), rs.getString(2), rs.getDouble(3)})) {
            skinStats.computeIfAbsent((Integer) r[0], k -> new LinkedHashMap<>()).put((String) r[1], (Double) r[2]);
        }
        Map<Integer, StaticData.Skin> skins = new LinkedHashMap<>();
        for (var s : rows(c, "SELECT costume_id, name, gender_id, kind_key, rarity_key, gem_price, fusion_race, ai_can_wear, enabled FROM skin_template",
                rs -> new StaticData.Skin(rs.getInt(1), rs.getString(2), rs.getInt(3), rs.getString(4), rs.getString(5),
                        (Integer) nullableInt(rs, 6), rs.getString(7), rs.getBoolean(8), rs.getBoolean(9), Map.of()))) {
            skins.put(s.id(), new StaticData.Skin(s.id(), s.name(), s.genderId(), s.kindKey(), s.rarity(), s.gemPrice(), s.fusionRace(),
                    s.aiCanWear(), s.enabled(), Map.copyOf(skinStats.getOrDefault(s.id(), Map.of()))));
        }

        Map<Integer, Map<Integer, StaticData.SkillLevel>> levels = new HashMap<>();
        for (Object[] r : rows(c, "SELECT skill_id, `level`, damage_pct_atk, damage_pct_max_ki, ki_cost_pct, cooldown_ms, COALESCE(gold_cost, 0), "
                + "length_px, width_px, radius_px, projectile_speed, params FROM skill_level", rs -> new Object[]{rs.getInt(1),
                new StaticData.SkillLevel(rs.getInt(2), (Double) nullableDouble(rs, 3), (Double) nullableDouble(rs, 4), (Double) nullableDouble(rs, 5),
                        rs.getInt(6), rs.getLong(7), (Integer) nullableInt(rs, 8), (Integer) nullableInt(rs, 9), (Integer) nullableInt(rs, 10),
                        (Integer) nullableInt(rs, 11), Json.parseObject(rs.getString(12)))})) {
            var lv = (StaticData.SkillLevel) r[1];
            levels.computeIfAbsent((Integer) r[0], k -> new TreeMap<>()).put(lv.level(), lv);
        }
        Map<Integer, StaticData.Skill> skills = new LinkedHashMap<>();
        for (var s : rows(c, "SELECT skill_id, race, name, kind, ki_cost_mode, hit_shape, slot, max_level, enabled, icon_id, dam_info FROM skill_template ORDER BY race, slot",
                rs -> new StaticData.Skill(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), rs.getString(5), rs.getString(6),
                        rs.getInt(7), rs.getInt(8), rs.getBoolean(9), Map.of(), rs.getInt(10), rs.getString(11) == null ? "" : rs.getString(11)))) {
            skills.put(s.id(), new StaticData.Skill(s.id(), s.race(), s.name(), s.kind(), s.kiMode(), s.hitShape(), s.slot(), s.maxLevel(), s.enabled(),
                    Map.copyOf(levels.getOrDefault(s.id(), Map.of())), s.iconId(), s.damInfo()));
        }

        Map<Integer, StaticData.ItemTemplate> items = new LinkedHashMap<>();
        for (var i : rows(c, "SELECT template_id, item_key, slot, name_vi, tier, base_stat_key, base_value, pieces_needed, min_level, enabled, icon_id, note, race FROM item_template",
                rs -> new StaticData.ItemTemplate(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), rs.getInt(5), rs.getString(6),
                        rs.getDouble(7), rs.getInt(8), rs.getInt(9), rs.getBoolean(10), rs.getInt(11), rs.getString(12), rs.getString(13)))) {
            items.put(i.id(), i);
        }
        Map<Integer, StaticData.Material> materials = new LinkedHashMap<>();
        for (var m : rows(c, "SELECT template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, enabled, icon_id, note FROM material_template",
                rs -> new StaticData.Material(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), (Integer) nullableInt(rs, 5),
                        rs.getString(6), (Double) nullableDouble(rs, 7), (Integer) nullableInt(rs, 8), rs.getBoolean(9), rs.getInt(10), rs.getString(11)))) {
            materials.put(m.id(), m);
        }

        Map<Integer, Map<String, Double>> supStats = new HashMap<>();
        for (Object[] r : rows(c, "SELECT support_id, stat_key, `value` FROM support_item_stat", rs -> new Object[]{rs.getInt(1), rs.getString(2), rs.getDouble(3)})) {
            supStats.computeIfAbsent((Integer) r[0], k -> new LinkedHashMap<>()).put((String) r[1], (Double) r[2]);
        }
        Map<Integer, StaticData.PetEffect> pets = new HashMap<>();
        for (Object[] r : rows(c, "SELECT support_id, stat_key, `value`, interval_s, duration_s FROM support_pet_effect",
                rs -> new Object[]{rs.getInt(1), new StaticData.PetEffect(rs.getString(2), rs.getDouble(3), rs.getInt(4), rs.getInt(5))})) {
            pets.put((Integer) r[0], (StaticData.PetEffect) r[1]);
        }
        Map<Integer, StaticData.TrainArmor> trains = new HashMap<>();
        for (Object[] r : rows(c, "SELECT support_id, level, atk_penalty_pct, atk_bonus_pct, points_per_match, max_points FROM support_train_armor",
                rs -> new Object[]{rs.getInt(1), new StaticData.TrainArmor(rs.getInt(2), rs.getDouble(3), rs.getDouble(4), rs.getInt(5), rs.getInt(6))})) {
            trains.put((Integer) r[0], (StaticData.TrainArmor) r[1]);
        }
        Map<Integer, Integer> bookLines = new HashMap<>();
        for (Object[] r : rows(c, "SELECT support_id, lines_count FROM skill_book_template", rs -> new Object[]{rs.getInt(1), rs.getInt(2)})) {
            bookLines.put((Integer) r[0], (Integer) r[1]);
        }
        Map<Integer, StaticData.SupportItem> support = new LinkedHashMap<>();
        for (var s : rows(c, "SELECT support_id, kind_key, race, name_vi, obtain, gem_price, enabled FROM support_item_template",
                rs -> new Object[]{rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), rs.getString(5), nullableInt(rs, 6), rs.getBoolean(7)})) {
            int id = (Integer) s[0];
            support.put(id, new StaticData.SupportItem(id, (String) s[1], (String) s[2], (String) s[3], (String) s[4], (Integer) s[5], (Boolean) s[6],
                    Map.copyOf(supStats.getOrDefault(id, Map.of())), pets.get(id), trains.get(id), bookLines.getOrDefault(id, 1)));
        }
        List<StaticData.BookPoolEntry> pool = rows(c, "SELECT stat_key, min_value, max_value, weight FROM skill_book_pool WHERE enabled = 1",
                rs -> new StaticData.BookPoolEntry(rs.getString(1), rs.getDouble(2), rs.getDouble(3), rs.getInt(4)));

        Map<Integer, List<StaticData.RecipeInput>> recipeInputs = new HashMap<>();
        for (Object[] r : rows(c, "SELECT recipe_id, material_template_id, quantity, lose_on_fail_pct FROM craft_recipe_input",
                rs -> new Object[]{rs.getInt(1), new StaticData.RecipeInput(rs.getInt(2), rs.getInt(3), rs.getDouble(4))})) {
            recipeInputs.computeIfAbsent((Integer) r[0], k -> new ArrayList<>()).add((StaticData.RecipeInput) r[1]);
        }
        List<StaticData.Recipe> recipes = rows(c, "SELECT recipe_id, name_vi, recipe_type, input_support_id, result_support_id, success_pct, boost_material_id, "
                        + "boost_success_pct, extra_line_chance_pct, extra_lines_min, extra_lines_max, enabled FROM craft_recipe",
                rs -> new StaticData.Recipe(rs.getInt(1), rs.getString(2), rs.getString(3), (Integer) nullableInt(rs, 4), rs.getInt(5), rs.getDouble(6),
                        (Integer) nullableInt(rs, 7), (Double) nullableDouble(rs, 8), rs.getDouble(9), rs.getInt(10), rs.getInt(11), rs.getBoolean(12),
                        List.of()));
        recipes = recipes.stream().map(r -> new StaticData.Recipe(r.id(), r.name(), r.type(), r.inputSupportId(), r.resultSupportId(), r.successPct(),
                r.boostMaterialId(), r.boostSuccessPct(), r.extraLineChancePct(), r.extraLinesMin(), r.extraLinesMax(), r.enabled(),
                List.copyOf(recipeInputs.getOrDefault(r.id(), List.of())))).toList();

        List<StaticData.DropRule> drops = rows(c, "SELECT source_class, tier, material_template_id, dungeon_id, rate_pct, qty_min, qty_max FROM drop_rule WHERE enabled = 1",
                rs -> new StaticData.DropRule(rs.getString(1), rs.getString(2), rs.getInt(3), (Integer) nullableInt(rs, 4), rs.getDouble(5), rs.getInt(6), rs.getInt(7)));
        Map<String, Double> mult = new HashMap<>();
        for (Object[] r : rows(c, "SELECT dungeon_id, material_template_id, mult FROM dungeon_drop_mult", rs -> new Object[]{rs.getInt(1), rs.getInt(2), rs.getDouble(3)})) {
            mult.put(r[0] + ":" + r[1], (Double) r[2]);
        }
        List<StaticData.GemRule> gems = rows(c, "SELECT reward_type, dungeon_id, gems, daily_cap, enabled FROM gem_reward_rule",
                rs -> new StaticData.GemRule(rs.getString(1), (Integer) nullableInt(rs, 2), rs.getInt(3), (Integer) nullableInt(rs, 4), rs.getBoolean(5)));
        List<StaticData.Achievement> achievements = rows(c, "SELECT achievement_id, name_vi, race, condition_type, threshold, reward_costume_id, reward_gems "
                        + "FROM achievement_template WHERE enabled = 1",
                rs -> new StaticData.Achievement(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), rs.getInt(5), (Integer) nullableInt(rs, 6),
                        (Integer) nullableInt(rs, 7)));

        Map<String, List<Integer>> defaults = new HashMap<>();
        Map<String, Integer> equipped = new HashMap<>();
        for (Object[] r : rows(c, "SELECT race, costume_id, is_equipped FROM race_default_skin ORDER BY race, costume_id",
                rs -> new Object[]{rs.getString(1), rs.getInt(2), rs.getBoolean(3)})) {
            defaults.computeIfAbsent((String) r[0], k -> new ArrayList<>()).add((Integer) r[1]);
            if ((Boolean) r[2]) {
                equipped.put((String) r[0], (Integer) r[1]);
            }
        }

        Map<Integer, List<StaticData.DungeonBoss>> dungeonBosses = new HashMap<>();
        for (Object[] r : rows(c, "SELECT dungeon_id, boss_id, role, mob_tier, sort_order FROM dungeon_boss ORDER BY dungeon_id, sort_order",
                rs -> new Object[]{rs.getInt(1), new StaticData.DungeonBoss(rs.getInt(2), rs.getString(3), rs.getString(4), rs.getInt(5))})) {
            dungeonBosses.computeIfAbsent((Integer) r[0], k -> new ArrayList<>()).add((StaticData.DungeonBoss) r[1]);
        }
        Map<Integer, StaticData.Dungeon> dungeons = new LinkedHashMap<>();
        for (var d : rows(c, "SELECT dungeon_id, name_vi, tier, bosses_per_match, time_limit_s, group_key, enabled FROM dungeon_template ORDER BY sort_order",
                rs -> new StaticData.Dungeon(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getInt(4), (Integer) nullableInt(rs, 5), rs.getString(6),
                        rs.getBoolean(7), List.of()))) {
            dungeons.put(d.id(), new StaticData.Dungeon(d.id(), d.name(), d.tier(), d.bossesPerMatch(), d.timeLimitS(), d.groupKey(), d.enabled(),
                    List.copyOf(dungeonBosses.getOrDefault(d.id(), List.of()))));
        }
        Map<Integer, StaticData.Boss> bosses = new LinkedHashMap<>();
        for (var b : rows(c, "SELECT boss_id, group_key, name_vi, race, part_head, part_body, part_leg, hp_mult, atk_mult, enabled FROM boss_template",
                rs -> new StaticData.Boss(rs.getInt(1), rs.getString(2), rs.getString(3), rs.getString(4), rs.getInt(5), rs.getInt(6), rs.getInt(7),
                        rs.getDouble(8), rs.getDouble(9), rs.getBoolean(10)))) {
            bosses.put(b.id(), b);
        }
        Map<String, StaticData.MonsterStat> monsters = new LinkedHashMap<>();
        for (var m : rows(c, "SELECT class_key, tier, ttk_seconds, hp, atk_pct_min, atk_pct_max, armor_pct_min, armor_pct_max, action_gap_s FROM monster_stat_class",
                rs -> new StaticData.MonsterStat(rs.getString(1), rs.getString(2), rs.getDouble(3), rs.getLong(4), rs.getDouble(5), rs.getDouble(6),
                        rs.getDouble(7), rs.getDouble(8), (Double) nullableDouble(rs, 9)))) {
            monsters.put(m.classKey() + ":" + m.tier(), m);
        }

        return new StaticData(versions, Map.copyOf(balance), Map.copyOf(stats), Map.copyOf(kinds), Map.copyOf(skins), Map.copyOf(skills), Map.copyOf(items),
                Map.copyOf(materials), Map.copyOf(support), List.copyOf(pool), recipes, List.copyOf(drops), Map.copyOf(mult), List.copyOf(gems),
                List.copyOf(achievements), Map.copyOf(defaults), Map.copyOf(equipped), Map.copyOf(dungeons), Map.copyOf(bosses), Map.copyOf(monsters));
    }

    private static <T> List<T> rows(Connection c, String sql, RowMapper<T> mapper) throws SQLException {
        List<T> out = new ArrayList<>();
        try (PreparedStatement ps = c.prepareStatement(sql); ResultSet rs = ps.executeQuery()) {
            while (rs.next()) {
                out.add(mapper.map(rs));
            }
        }
        return out;
    }

    private static Object nullableDouble(ResultSet rs, int col) throws SQLException {
        double v = rs.getDouble(col);
        return rs.wasNull() ? null : v;
    }

    private static Object nullableInt(ResultSet rs, int col) throws SQLException {
        int v = rs.getInt(col);
        return rs.wasNull() ? null : v;
    }
}
