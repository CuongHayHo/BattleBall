package com.battleball.server.game;

import com.battleball.proto.ShopContent;
import com.battleball.proto.ShopItem;
import com.battleball.proto.ShopTab;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

/**
 * What an NPC sells (docs/architecture/BattleBallServer.md 15.15). The shops are listed in balance_config 'npc_shops' (V033): one key per NPC id with its name
 * and tabs. The skins on sale are every enabled, sellable skin that has a gem price (the same rule {@link EconomyService#buySkin} applies when the player buys),
 * so the list and the purchase can never disagree. Teamobi's ShopService sends the shop when the NPC is tapped (cmd -44) and validates the purchase against it;
 * here the purchase is the ordinary BuySkinRequest, which checks the skin again.
 */
public final class ShopService {
    private ShopService() {
    }

    /** The shop of this NPC, or null when the NPC sells nothing. */
    public static ShopContent content(StaticData d, String npcId) {
        Map<String, Object> shop = Json.obj(d.balanceObj("npc_shops"), npcId == null ? "" : npcId);
        if (shop.isEmpty()) {
            return null;
        }
        List<StaticData.Skin> onSale = new ArrayList<>();
        for (StaticData.Skin skin : d.skins().values()) {
            StaticData.SkinKind kind = d.skinKinds().get(skin.kindKey());
            if (kind != null && skin.enabled() && kind.enabled() && kind.sellable() && skin.gemPrice() != null) {
                onSale.add(skin);
            }
        }
        onSale.sort(Comparator.comparingInt((StaticData.Skin s) -> s.gemPrice()).thenComparingInt(StaticData.Skin::id));
        ShopContent.Builder out = ShopContent.newBuilder().setNpcId(npcId).setNpcName(shop.get("name") instanceof String n ? n : npcId);
        if (shop.get("tabs") instanceof List<?> tabs) {
            for (Object t : tabs) {
                if (!(t instanceof Map<?, ?>)) {
                    continue;
                }
                @SuppressWarnings("unchecked")
                Map<String, Object> tab = (Map<String, Object>) t;
                int gender = (int) Json.num(tab, "gender", 3);
                ShopTab.Builder b = ShopTab.newBuilder().setName(tab.get("name") instanceof String n ? n : "");
                for (StaticData.Skin skin : onSale) {
                    if (skin.genderId() == gender) {
                        b.addItems(ShopItem.newBuilder().setSkin(PlayerRepository.skinProto(d, skin.id())).setGemPrice(skin.gemPrice()));
                    }
                }
                if (b.getItemsCount() > 0) {
                    out.addTabs(b);
                }
            }
        }
        return out.build();
    }
}
