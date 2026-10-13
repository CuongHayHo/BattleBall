package com.battleball.server;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

import java.sql.Connection;
import java.sql.Statement;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

class StaticDataTest {

    @Test
    void jsonParser() {
        Map<String, Object> m = Json.parseObject("{\"a\": 1.5, \"b\": [1, 2, {\"c\": null}], \"d\": \"x\\\"y\\u0041\", \"e\": true, \"f\": {\"g\": -3e2}}");
        assertEquals(1.5, Json.num(m, "a", 0));
        List<?> arr = (List<?>) m.get("b");
        assertEquals(3, arr.size());
        assertEquals(2.0, arr.get(1));
        assertTrue(((Map<?, ?>) arr.get(2)).containsKey("c") && ((Map<?, ?>) arr.get(2)).get("c") == null);
        assertEquals("x\"yA", m.get("d"));
        assertTrue(Json.bool(m, "e"));
        assertEquals(-300.0, Json.num(Json.obj(m, "f"), "g", 0));
        assertEquals(20.0, Json.parse("20"));
        assertNull(Json.parse(""));
    }

    @Test
    @EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
    void loadsTheWholeSnapshotAndReloadsOnVersionChange() throws Exception {
        try (TestDb t = TestDb.create(); Database db = new Database(t.url, t.user, t.password, 2)) {
            StaticDataStore store = new StaticDataStore(new StaticDataLoader(db));
            StaticData d = store.get();
            assertEquals(353, d.skins().size());
            assertEquals(51, d.stats().size());
            assertTrue(d.statEnabled("ATK_PCT"));
            assertEquals("Sức đánh +100%", d.statText("ATK_PCT", 100));
            assertEquals("HP +12,5%", d.statText("HP_PCT", 12.5));
            assertEquals("Hồi HP +3% mỗi giây", d.statText("HP_REGEN_PCT", 3), "the NRO remark of the admin name is dropped");
            assertEquals(25, d.skills().size());
            assertEquals(7, d.skills().get(1).levels().size());
            assertEquals(450.0, d.skills().get(1).level(7).damagePctAtk());
            assertEquals(500_000.0, d.balanceNum("max_atk_total", 0));
            assertEquals(45, d.items().size(), "three sets of five pieces for each of the three planets");
            assertEquals(20000.0, d.items().get(3).baseValue());
            assertTrue(d.materials().containsKey(301));
            for (int m : new int[]{101, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 202, 203, 204, 205}) {
                final int material = m;
                assertTrue(d.dropRules().stream().anyMatch(r -> r.materialId() == material), "something drops material " + material + " (V016, V041)");
            }
            assertTrue(d.dropRules().stream().noneMatch(r -> r.materialId() >= 111 && r.materialId() <= 115 && !r.tier().equals("late")), "the Thien Su fragments drop in the late dungeons only");
            assertTrue(d.dropRules().stream().noneMatch(r -> r.materialId() >= 106 && r.materialId() <= 110 && !(r.tier().equals("mid") || r.tier().equals("late"))), "the Huy Diet fragments start in the middle dungeons");
            assertEquals(3, com.battleball.server.game.StatStones.rules(d).maxLines());
            assertEquals(6, com.battleball.server.game.StatStones.rules(d).types().size());
            assertEquals(6, d.recipes().size());
            assertEquals(2, d.recipes().get(0).inputs().size());
            assertEquals(13, d.dungeons().size());
            assertEquals(146, d.bosses().size());
            assertEquals(15, d.monsterStats().size());
            assertEquals(6_042_094L, d.monsterStat("BOSS", "early").hp());
            assertEquals(3.0, d.dropMult(3, 301));
            assertEquals(1.0, d.dropMult(4, 301));
            assertEquals(List.of(412, 413, 414), d.defaultSkinsByRace().get("EARTH"));
            assertEquals(412, d.equippedDefaultSkinByRace().get("EARTH"));
            assertNotNull(d.supportItems().get(1224).pet());
            assertEquals(30, d.supportItems().get(1224).pet().durationS() * 3);
            assertEquals(10, d.dungeons().get(3).bosses().size(), "seven bosses and the three Halloween small monsters of V040");
            assertTrue(d.dungeons().values().stream().allMatch(dg -> dg.bosses().stream().anyMatch(b -> b.role().equals("MINION"))), "every dungeon has small monsters");
            assertEquals("Xên – Xên con", d.dungeons().get(6).name());
            assertFalse(store.refreshIfChanged(), "nothing changed yet");

            try (Connection c = db.connection(); Statement st = c.createStatement()) {
                st.execute("UPDATE monster_stat_class SET hp = 1234567 WHERE class_key = 'BOSS' AND tier = 'early'");
                st.execute("UPDATE static_data_version SET version = version + 1 WHERE domain = 'MONSTER'");
            }
            assertTrue(store.refreshIfChanged());
            assertEquals(1_234_567L, store.get().monsterStat("BOSS", "early").hp());
            assertFalse(store.refreshIfChanged());
            assertEquals(6_042_094L, d.monsterStat("BOSS", "early").hp(), "the old snapshot is not modified");
        }
    }

    @Test
    @EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
    void theDungeonsAreNamedAndDescribeTheirBossesMonstersAndDrops() throws Exception {
        try (TestDb t = TestDb.create(); Database db = new Database(t.url, t.user, t.password, 2)) {
            StaticData d = new StaticDataLoader(db).load();
            for (StaticData.Dungeon dg : d.dungeons().values()) {
                if (!dg.enabled()) {
                    continue;
                }
                var what = com.battleball.server.game.DungeonSummary.of(d, dg);
                String where = "dungeon " + dg.id() + " " + dg.name();
                assertFalse(dg.name().startsWith("Phó bản"), where + " has a name of what is in it, not a number");
                assertFalse(what.finalBoss().isEmpty(), where + " has a final boss");
                assertFalse(what.minions().isEmpty(), where + " has small monsters");
                assertFalse(what.drops().isEmpty(), where + " drops something");
                for (String n : what.bosses()) {
                    assertFalse(n.contains("code"), "a boss name has no note of the source data: " + n);
                }
                assertFalse(what.finalBoss().contains("code"), where + " final boss " + what.finalBoss());
            }
            var early = com.battleball.server.game.DungeonSummary.of(d, d.dungeons().get(1));
            assertFalse(early.drops().stream().anyMatch(x -> x.contains("Hủy Diệt") || x.contains("Thiên Sứ")), "an early dungeon drops the first set only: " + early.drops());
            assertTrue(early.drops().stream().anyMatch(x -> x.contains("Thần Linh")));
            var mid = com.battleball.server.game.DungeonSummary.of(d, d.dungeons().get(4));
            assertTrue(mid.drops().stream().anyMatch(x -> x.contains("Hủy Diệt")) && mid.drops().stream().noneMatch(x -> x.contains("Thiên Sứ")), "a middle dungeon adds the Hủy Diệt pieces: " + mid.drops());
            var late = com.battleball.server.game.DungeonSummary.of(d, d.dungeons().get(7));
            assertTrue(late.drops().stream().anyMatch(x -> x.contains("Thiên Sứ")), "a late dungeon adds the Thiên Sứ pieces: " + late.drops());
            var apes = com.battleball.server.game.DungeonSummary.of(d, d.dungeons().get(3));
            assertTrue(apes.drops().stream().anyMatch(x -> x.contains("Nguyên liệu sách") && x.contains("(x3)")), "the ape dungeon raises the book materials three times: " + apes.drops());
            var earring = com.battleball.server.game.DungeonSummary.of(d, d.dungeons().get(11));
            assertTrue(earring.drops().stream().anyMatch(x -> x.contains("bông tai")), "dungeon 11 drops earring shards: " + earring.drops());
            assertEquals("Kid Bư", earring.finalBoss());
        }
    }
}
