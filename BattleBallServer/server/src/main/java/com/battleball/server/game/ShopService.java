package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.ShopContent;
import com.battleball.proto.ShopItem;
import com.battleball.proto.ShopTab;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * What an NPC sells (docs/architecture/BattleBallServer.md 15.15, 15.17). The shops are listed in balance_config 'npc_shops' (V033, V035): one key per NPC id with its name,
 * the portrait and its tabs; a tab says what it sells ({@code sells}):
 * <ul>
 * <li>{@code SKINS} (the default): every enabled, sellable skin with a gem price and the race of the tab ({@code gender}); the same rule {@link EconomyService#buySkin} applies;</li>
 * <li>{@code GEAR}: the five pieces of one set of equipment (the {@code tier} of the tab: 1 Thần Linh, 2 Hủy Diệt, 3 Thiên Sứ; Áo, Quần, Găng, Giày, Nhẫn), each a whole piece for the gold
 * of its 10 fragments ({@link EconomyService#buyGear});</li>
 * <li>{@code EXCHANGE}: every piece of every set, for the 10 fragments of its own the account has collected, no gold ({@link EconomyService#assembleItem});</li>
 * <li>{@code STONES}: the upgrade stone, the stat stone (gold) and the crystal stone (gems) of the plan (GDD 6), bought by the piece ({@link EconomyService#buyMaterial}).</li>
 * </ul>
 * Prices that depend on the level (the gold of a win grows with it) are those of the account's highest character, and the purchase uses that same character, so the list and the
 * purchase agree. Teamobi's ShopService sends the shop when the NPC is tapped (cmd -44) and validates the purchase against the shop the player has open; here the purchase names the
 * NPC and the server checks that the item is really on sale in that shop ({@link #sells}).
 */
public final class ShopService {
    private static final List<String> GEAR_SLOTS = List.of("ARMOR", "PANTS", "GLOVES", "BOOTS", "RING");
    private static final Set<String> STONE_KINDS = Set.of("UPGRADE_STONE", "STAT_STONE", "CRYSTAL_STONE", "GEN_STONE", "GEN_SLOT_STONE");

    private ShopService() {
    }

    /** The shop of this NPC priced for a character of this level, or null when the NPC sells nothing. */
    public static ShopContent content(StaticData d, String npcId, int level) {
        Map<String, Object> shop = Json.obj(d.balanceObj("npc_shops"), npcId == null ? "" : npcId);
        if (shop.isEmpty()) {
            return null;
        }
        ShopContent.Builder out = ShopContent.newBuilder().setNpcId(npcId).setNpcName(shop.get("name") instanceof String n ? n : npcId)
                .setPortraitIcon((int) Json.num(shop, "portrait_icon", 0));
        if (shop.get("tabs") instanceof List<?> tabs) {
            for (Object t : tabs) {
                if (!(t instanceof Map<?, ?>)) {
                    continue;
                }
                @SuppressWarnings("unchecked")
                Map<String, Object> tab = (Map<String, Object>) t;
                ShopTab.Builder b = ShopTab.newBuilder().setName(tab.get("name") instanceof String n ? n : "");
                String sells = tab.get("sells") instanceof String s ? s : "SKINS";
                switch (sells) {
                    case "GEAR" -> addGear(d, b, level, (int) Json.num(tab, "tier", 0));
                    case "EXCHANGE" -> addExchange(d, b);
                    case "STONES" -> addStones(d, b, level);
                    default -> addSkins(d, b, (int) Json.num(tab, "gender", 3));
                }
                if (b.getItemsCount() > 0) {
                    out.addTabs(b);
                }
            }
        }
        return out.build();
    }

    private static void addSkins(StaticData d, ShopTab.Builder b, int gender) {
        List<StaticData.Skin> onSale = new ArrayList<>();
        for (StaticData.Skin skin : d.skins().values()) {
            StaticData.SkinKind kind = d.skinKinds().get(skin.kindKey());
            if (kind != null && skin.enabled() && kind.enabled() && kind.sellable() && skin.gemPrice() != null && skin.genderId() == gender) {
                onSale.add(skin);
            }
        }
        onSale.sort(Comparator.comparingInt((StaticData.Skin s) -> s.gemPrice()).thenComparingInt(StaticData.Skin::id));
        for (StaticData.Skin skin : onSale) {
            b.addItems(ShopItem.newBuilder().setKind("SKIN").setSkin(PlayerRepository.skinProto(d, skin.id())).setGemPrice(skin.gemPrice()));
        }
    }

    /** The fragment material that makes a piece of gear (sold for gold), or null. The fragments are one kind per piece of a set, for every planet ({@link StaticData#fragmentRoot}). */
    private static StaticData.Material fragmentOf(StaticData d, StaticData.ItemTemplate t) {
        int root = d.fragmentRoot(t).id();
        return d.materials().values().stream()
                .filter(m -> m.kind().equals("FRAGMENT") && Integer.valueOf(root).equals(m.itemTemplateId()) && m.enabled() && m.buyCurrency().equals("GOLD"))
                .findFirst().orElse(null);
    }

    private static final List<String> RACES = List.of("EARTH", "NAMEC", "XAYDA");

    private static String planetName(String race) {
        return race.equals("EARTH") ? "Trái Đất" : race.equals("NAMEC") ? "Namếc" : "Xayda";
    }

    /** The pieces of gear that have fragments, of one tier ({@code tier} 0 = every tier), by tier, planet (Trái Đất, Namếc, Xayda), slot. */
    private static List<StaticData.ItemTemplate> gearPieces(StaticData d, int tier) {
        List<StaticData.ItemTemplate> pieces = new ArrayList<>();
        for (StaticData.ItemTemplate t : d.items().values()) {
            if (t.enabled() && GEAR_SLOTS.contains(t.slot()) && fragmentOf(d, t) != null && (tier == 0 || t.tier() == tier)) {
                pieces.add(t);
            }
        }
        pieces.sort(Comparator.comparingInt(StaticData.ItemTemplate::tier).thenComparingInt((StaticData.ItemTemplate t) -> RACES.indexOf(t.race()))
                .thenComparingInt((StaticData.ItemTemplate t) -> GEAR_SLOTS.indexOf(t.slot())).thenComparingInt(StaticData.ItemTemplate::id));
        return pieces;
    }

    private static String levelText(StaticData.ItemTemplate t) {
        return t.minLevel() > 1 ? "\nCần cấp " + t.minLevel() + "." : "";
    }

    private static void addGear(StaticData d, ShopTab.Builder b, int level, int tier) {
        for (StaticData.ItemTemplate t : gearPieces(d, tier)) {
            long gold = EconomyService.goldUnitPrice(d, fragmentOf(d, t), level) * t.piecesNeeded();
            String text = d.statText(t.baseStatKey(), t.baseValue()) + "\nDành cho " + planetName(t.race()) + ". Ghép từ " + t.piecesNeeded() + " mảnh đồ, mua đủ một món." + levelText(t);
            b.addItems(ShopItem.newBuilder().setKind("GEAR").setTemplateId(t.id()).setName(t.name()).setDescription(text).setIconId(t.iconId())
                    .setGoldPrice(gold).setMinLevel(t.minLevel()));
        }
    }

    /** One row per piece of every set: the fragments of that piece (10) in exchange for the piece; the client shows how many the account has. */
    private static void addExchange(StaticData d, ShopTab.Builder b) {
        for (StaticData.ItemTemplate t : gearPieces(d, 0)) {
            StaticData.Material frag = fragmentOf(d, t);
            String text = d.statText(t.baseStatKey(), t.baseValue()) + "\nDành cho " + planetName(t.race()) + ". Đổi " + t.piecesNeeded() + " " + frag.name() + " lấy món này." + levelText(t);
            b.addItems(ShopItem.newBuilder().setKind("EXCHANGE").setTemplateId(t.id()).setName(t.name()).setDescription(text).setIconId(t.iconId())
                    .setMinLevel(t.minLevel()).setFragmentTemplateId(frag.id()).setFragmentCost(t.piecesNeeded()).setFragmentIconId(frag.iconId()));
        }
    }

    private static List<StaticData.Material> stonesOnSale(StaticData d) {
        List<StaticData.Material> stones = new ArrayList<>();
        for (StaticData.Material m : d.materials().values()) {
            if (m.enabled() && STONE_KINDS.contains(m.kind()) && !m.buyCurrency().equals("NONE")) {
                stones.add(m);
            }
        }
        stones.sort(Comparator.comparingInt(StaticData.Material::id));
        return stones;
    }

    private static void addStones(StaticData d, ShopTab.Builder b, int level) {
        for (StaticData.Material m : stonesOnSale(d)) {
            ShopItem.Builder item = ShopItem.newBuilder().setKind("MATERIAL").setTemplateId(m.id()).setName(m.name()).setIconId(m.iconId())
                    .setDescription(m.note() == null ? "" : m.note());
            if (m.buyCurrency().equals("GOLD")) {
                item.setGoldPrice(EconomyService.goldUnitPrice(d, m, level));
            } else if (m.priceFixed() != null) {
                item.setGemPrice(m.priceFixed());
            }
            b.addItems(item);
        }
    }

    /** Whether the shop of this NPC sells this gear piece or stone (the check a purchase makes before it spends anything). */
    public static boolean sells(StaticData d, String npcId, String kind, int templateId) {
        ShopContent shop = content(d, npcId, 1);
        if (shop == null) {
            return false;
        }
        for (ShopTab tab : shop.getTabsList()) {
            for (ShopItem item : tab.getItemsList()) {
                if (item.getKind().equals(kind) && item.getTemplateId() == templateId) {
                    return true;
                }
            }
        }
        return false;
    }

    /** The error for a purchase the shop does not allow. */
    public static ActionResult notOnSale() {
        return ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("this shop does not sell that").build();
    }
}
