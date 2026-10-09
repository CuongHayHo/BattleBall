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
            assertEquals(221, d.skins().size());
            assertEquals(51, d.stats().size());
            assertTrue(d.statEnabled("ATK_PCT"));
            assertEquals("Sức đánh +100%", d.statText("ATK_PCT", 100));
            assertEquals("HP +12,5%", d.statText("HP_PCT", 12.5));
            assertEquals("Hồi HP +3% mỗi giây", d.statText("HP_REGEN_PCT", 3), "the NRO remark of the admin name is dropped");
            assertEquals(25, d.skills().size());
            assertEquals(7, d.skills().get(1).levels().size());
            assertEquals(450.0, d.skills().get(1).level(7).damagePctAtk());
            assertEquals(500_000.0, d.balanceNum("max_atk_total", 0));
            assertEquals(5, d.items().size());
            assertEquals(20000.0, d.items().get(3).baseValue());
            assertTrue(d.materials().containsKey(301));
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
            assertEquals(7, d.dungeons().get(3).bosses().size());
            assertEquals("Phó bản 6", d.dungeons().get(6).name());
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
}
