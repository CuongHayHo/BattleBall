package com.battleball.server.game;

import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The auras of the game (V029, balance_config "auras"): an aura is unlocked when the highest character of the account reaches its level, the account wears one at a time
 * and it is only a picture around the characters.
 */
public final class Auras {
    private Auras() {
    }

    public record Layer(int layer, int frames) {
    }

    public record Aura(int id, String name, int level, boolean enabled, List<Layer> layers) {
    }

    /** The auras in the data, ordered by id; an entry without an id or a level is skipped. */
    @SuppressWarnings("unchecked")
    public static List<Aura> all(StaticData d) {
        List<Aura> out = new ArrayList<>();
        Object items = d.balanceObj("auras").get("items");
        if (!(items instanceof List<?> list)) {
            return out;
        }
        for (Object o : list) {
            if (!(o instanceof Map<?, ?>)) {
                continue;
            }
            Map<String, Object> m = (Map<String, Object>) o;
            int id = (int) Json.num(m, "id", 0);
            int level = (int) Json.num(m, "level", 0);
            if (id <= 0 || level <= 0) {
                continue;
            }
            List<Layer> layers = new ArrayList<>();
            if (m.get("layers") instanceof List<?> ls) {
                for (Object lo : ls) {
                    if (lo instanceof Map<?, ?>) {
                        Map<String, Object> lm = (Map<String, Object>) lo;
                        layers.add(new Layer((int) Json.num(lm, "layer", 0), Math.max(1, (int) Json.num(lm, "frames", 1))));
                    }
                }
            }
            String name = m.get("name") instanceof String s ? s : "";
            out.add(new Aura(id, name, level, !Boolean.FALSE.equals(m.get("enabled")), layers));
        }
        out.sort((a, b) -> Integer.compare(a.id(), b.id()));
        return out;
    }

    public static Aura find(StaticData d, int id) {
        for (Aura a : all(d)) {
            if (a.id() == id) {
                return a;
            }
        }
        return null;
    }
}
