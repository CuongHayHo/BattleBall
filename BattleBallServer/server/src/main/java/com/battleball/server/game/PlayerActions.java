package com.battleball.server.game;

import com.battleball.proto.ActionError;
import com.battleball.proto.ActionResult;
import com.battleball.proto.Envelope;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.List;

/**
 * Maps an authenticated player's request message to the economy service and builds the reply messages.
 * Runs on the DB executor (it blocks). Each action returns an ActionResult for the request and, when it worked, a fresh PlayerDataSnapshot event.
 */
public final class PlayerActions {
    private static final Logger LOG = LoggerFactory.getLogger(PlayerActions.class);

    private final Database db;
    private final StaticDataStore data;
    private final EconomyService economy;
    private final MailService mail;
    private final AttendanceService attendance;
    private final QuestService quests;
    private volatile java.util.function.LongConsumer statsChanged = id -> { };

    /** Called (on the DB executor) after an action that can change the figures of a character; the match manager updates a running training session with it. */
    public void onStatsChanged(java.util.function.LongConsumer listener) {
        this.statsChanged = listener;
    }

    public PlayerActions(Database db, StaticDataStore data, EconomyService economy) {
        this.db = db;
        this.data = data;
        this.economy = economy;
        this.mail = new MailService(db, data);
        this.attendance = new AttendanceService(db, data);
        this.quests = new QuestService(db, data);
    }

    /**
     * Counts an event for the quests of the account (docs 15.32). Errors never reach the player: a quest that is not counted must not break the action that was paid.
     * Returns true when a counter changed.
     */
    public boolean recordQuest(long accountId, String type, int amount) {
        try {
            return quests.record(accountId, type, amount);
        } catch (SQLException | RuntimeException e) {
            LOG.warn("cannot count the quest event {}", type, e);
            return false;
        }
    }

    public static boolean handles(Envelope.PayloadCase c) {
        return switch (c) {
            case PLAYER_DATA_REQUEST, INVENTORY_REQUEST, BUY_SKIN_REQUEST, EQUIP_SKIN_REQUEST, UPGRADE_SKILL_REQUEST, BUY_MATERIAL_REQUEST,
                 ASSEMBLE_ITEM_REQUEST, EQUIP_ITEM_REQUEST, UNEQUIP_ITEM_REQUEST, SELL_ITEMS_REQUEST, UPGRADE_ITEM_REQUEST, BUY_SUPPORT_REQUEST,
                 EQUIP_SUPPORT_REQUEST, UNEQUIP_SUPPORT_REQUEST, CRAFT_REQUEST, BUY_POTARA_SHARDS_REQUEST, ASSEMBLE_POTARA_REQUEST,
                 MAIL_LIST_REQUEST, MAIL_ACTION_REQUEST, NOTICE_LIST_REQUEST, DISCARD_SKIN_REQUEST, EQUIP_AURA_REQUEST, SHOP_REQUEST, SHOP_BUY_REQUEST,
                 REROLL_GEN_REQUEST, UNLOCK_GEN_SLOT_REQUEST, STAT_STONE_REQUEST, CRYSTALIZE_REQUEST, ATTENDANCE_REQUEST, CLAIM_ATTENDANCE_REQUEST, CLAIM_QUEST_REQUEST -> true;
            default -> false;
        };
    }

    /** The replies for one request, in order. */
    public List<Envelope> handle(long accountId, Envelope req) {
        int rid = req.getRequestId();
        List<Envelope> out = new ArrayList<>();
        try {
            ActionResult r;
            switch (req.getPayloadCase()) {
                case PLAYER_DATA_REQUEST -> {
                    out.add(snapshotEnvelope(accountId, rid));
                    return out;
                }
                case INVENTORY_REQUEST -> {
                    var q = req.getInventoryRequest();
                    out.add(Envelope.newBuilder().setRequestId(rid).setInventoryPage(economy.inventory(accountId, q.getCharacterId(), q.getAfterItemId(), q.getPageSize())).build());
                    return out;
                }
                case MAIL_LIST_REQUEST -> {
                    out.add(Envelope.newBuilder().setRequestId(rid).setMailList(mail.list(accountId)).build());
                    return out;
                }
                case NOTICE_LIST_REQUEST -> {
                    out.add(Envelope.newBuilder().setRequestId(rid).setNoticeList(mail.notices()).build());
                    return out;
                }
                case SHOP_REQUEST -> {
                    var shop = ShopService.content(data.get(), req.getShopRequest().getNpcId(), topLevel(accountId));
                    out.add(shop != null ? Envelope.newBuilder().setRequestId(rid).setShopContent(shop).build()
                            : Envelope.newBuilder().setRequestId(rid).setActionResult(ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("this NPC sells nothing")).build());
                    return out;
                }
                case SHOP_BUY_REQUEST -> {
                    var q = req.getShopBuyRequest();
                    long buyer = topCharacter(accountId);
                    if (buyer == 0) {
                        r = ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("no character").build();
                    } else if (!ShopService.sells(data.get(), q.getNpcId(), q.getKind(), q.getTemplateId())) {
                        r = ShopService.notOnSale();
                    } else if (q.getKind().equals("GEAR")) {
                        r = economy.buyGear(accountId, buyer, q.getTemplateId());
                    } else if (q.getKind().equals("EXCHANGE")) {
                        r = economy.assembleItem(accountId, buyer, q.getTemplateId());
                    } else {
                        r = economy.buyMaterial(accountId, buyer, q.getTemplateId(), q.getQuantity());
                    }
                }
                case ATTENDANCE_REQUEST -> {
                    out.add(Envelope.newBuilder().setRequestId(rid).setAttendanceInfo(attendance.info(accountId)).build());
                    return out;
                }
                case CLAIM_ATTENDANCE_REQUEST -> {
                    r = attendance.claim(accountId);
                    if (r.getOk()) {
                        recordQuest(accountId, "ATTENDANCE", 1);
                    }
                    out.add(Envelope.newBuilder().setRequestId(rid).setActionResult(r).build());
                    if (r.getOk()) {
                        out.add(Envelope.newBuilder().setAttendanceInfo(attendance.info(accountId)).build());
                        out.add(snapshotEnvelope(accountId, 0));
                    }
                    return out;
                }
                case CLAIM_QUEST_REQUEST -> r = quests.claim(accountId, req.getClaimQuestRequest().getQuestId());
                case MAIL_ACTION_REQUEST -> {
                    var q = req.getMailActionRequest();
                    r = switch (q.getAction()) {
                        case MAIL_READ -> mail.read(accountId, q.getMailId());
                        case MAIL_CLAIM -> mail.claim(accountId, q.getMailId());
                        case MAIL_DELETE -> mail.delete(accountId, q.getMailId());
                        case MAIL_CLAIM_ALL -> mail.claimAll(accountId);
                        default -> ActionResult.newBuilder().setOk(false).setError(ActionError.INVALID_ARGUMENT).setMessage("unknown mail action").build();
                    };
                    out.add(Envelope.newBuilder().setRequestId(rid).setActionResult(r).build());
                    if (r.getOk()) {
                        out.add(Envelope.newBuilder().setMailList(mail.list(accountId)).build());
                        out.add(snapshotEnvelope(accountId, 0));
                    }
                    return out;
                }
                case BUY_SKIN_REQUEST -> r = economy.buySkin(accountId, req.getBuySkinRequest().getCharacterId(), req.getBuySkinRequest().getCostumeId());
                case EQUIP_SKIN_REQUEST -> r = economy.equipSkin(accountId, req.getEquipSkinRequest().getCharacterId(), req.getEquipSkinRequest().getCostumeId(), req.getEquipSkinRequest().getFusion());
                case EQUIP_AURA_REQUEST -> {
                    long any = anyCharacter(accountId);
                    r = any == 0 ? ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("no character").build()
                            : economy.equipAura(accountId, any, req.getEquipAuraRequest().getAuraId());
                }
                case DISCARD_SKIN_REQUEST -> {
                    long any = anyCharacter(accountId);
                    r = any == 0 ? ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("no character").build()
                            : economy.discardSkin(accountId, any, req.getDiscardSkinRequest().getCostumeId());
                }
                case UPGRADE_SKILL_REQUEST -> r = economy.upgradeSkill(accountId, req.getUpgradeSkillRequest().getCharacterId(), req.getUpgradeSkillRequest().getSkillId());
                case BUY_MATERIAL_REQUEST -> {
                    var q = req.getBuyMaterialRequest();
                    r = economy.buyMaterial(accountId, q.getCharacterId(), q.getTemplateId(), q.getQuantity());
                }
                case ASSEMBLE_ITEM_REQUEST -> r = economy.assembleItem(accountId, req.getAssembleItemRequest().getCharacterId(), req.getAssembleItemRequest().getItemTemplateId());
                case EQUIP_ITEM_REQUEST -> r = economy.equipItem(accountId, req.getEquipItemRequest().getCharacterId(), req.getEquipItemRequest().getItemId());
                case UNEQUIP_ITEM_REQUEST -> r = economy.unequipItem(accountId, req.getUnequipItemRequest().getCharacterId(), req.getUnequipItemRequest().getSlot());
                case SELL_ITEMS_REQUEST -> {
                    var q = req.getSellItemsRequest();
                    r = economy.sellItems(accountId, q.getCharacterId(), q.getItemIdsList(), q.getDestroy());
                }
                case UPGRADE_ITEM_REQUEST -> r = economy.upgradeItem(accountId, req.getUpgradeItemRequest().getCharacterId(), req.getUpgradeItemRequest().getItemId());
                case REROLL_GEN_REQUEST -> r = economy.rerollGen(accountId, req.getRerollGenRequest().getCharacterId(), req.getRerollGenRequest().getItemId(), req.getRerollGenRequest().getLineNo());
                case STAT_STONE_REQUEST -> {
                    var q = req.getStatStoneRequest();
                    r = economy.statStone(accountId, q.getCharacterId(), q.getItemId(), q.getAction(), q.getLineNo());
                }
                case CRYSTALIZE_REQUEST -> r = economy.crystalize(accountId, req.getCrystalizeRequest().getCharacterId(), req.getCrystalizeRequest().getItemId());
                case UNLOCK_GEN_SLOT_REQUEST -> r = economy.unlockGenSlot(accountId, req.getUnlockGenSlotRequest().getCharacterId(), req.getUnlockGenSlotRequest().getItemId());
                case BUY_SUPPORT_REQUEST -> r = economy.buySupport(accountId, req.getBuySupportRequest().getCharacterId(), req.getBuySupportRequest().getSupportId());
                case EQUIP_SUPPORT_REQUEST -> r = economy.equipSupport(accountId, req.getEquipSupportRequest().getCharacterId(), req.getEquipSupportRequest().getInstanceId());
                case UNEQUIP_SUPPORT_REQUEST -> r = economy.unequipSupport(accountId, req.getUnequipSupportRequest().getCharacterId(), req.getUnequipSupportRequest().getSlot());
                case CRAFT_REQUEST -> {
                    var q = req.getCraftRequest();
                    r = economy.craft(accountId, q.getCharacterId(), q.getRecipeId(), q.getInputInstanceId(), q.getUseBoost());
                }
                case BUY_POTARA_SHARDS_REQUEST -> r = economy.buyPotaraShards(accountId, req.getBuyPotaraShardsRequest().getCharacterId(), req.getBuyPotaraShardsRequest().getQuantity());
                case ASSEMBLE_POTARA_REQUEST -> {
                    long any = anyCharacter(accountId);
                    r = any == 0 ? ActionResult.newBuilder().setOk(false).setError(ActionError.NOT_FOUND).setMessage("no character").build() : economy.assemblePotara(accountId, any);
                }
                default -> r = ActionResult.newBuilder().setOk(false).setError(ActionError.INVALID_ARGUMENT).build();
            }
            if (r.getOk()) {
                countQuests(accountId, req);
            }
            out.add(Envelope.newBuilder().setRequestId(rid).setActionResult(r).build());
            if (r.getOk()) {
                out.add(snapshotEnvelope(accountId, 0));
                switch (req.getPayloadCase()) {
                    case EQUIP_ITEM_REQUEST, UNEQUIP_ITEM_REQUEST, UPGRADE_ITEM_REQUEST, REROLL_GEN_REQUEST, UNLOCK_GEN_SLOT_REQUEST, STAT_STONE_REQUEST, CRYSTALIZE_REQUEST, EQUIP_SKIN_REQUEST, EQUIP_AURA_REQUEST, EQUIP_SUPPORT_REQUEST, UNEQUIP_SUPPORT_REQUEST,
                         DISCARD_SKIN_REQUEST -> statsChanged.accept(accountId);
                    default -> { }
                }
            }
        } catch (SQLException | RuntimeException e) {
            LOG.error("player action failed", e);
            out.clear();
            out.add(Envelope.newBuilder().setRequestId(rid).setActionResult(ActionResult.newBuilder().setOk(false).setError(ActionError.ACTION_INTERNAL).setMessage("server error")).build());
        }
        return out;
    }

    /** What a successful request means to the quests (docs 15.32). */
    private void countQuests(long accountId, Envelope req) {
        switch (req.getPayloadCase()) {
            case SHOP_BUY_REQUEST -> {
                String kind = req.getShopBuyRequest().getKind();
                recordQuest(accountId, kind.equals("GEAR") || kind.equals("EXCHANGE") ? "GET_GEAR" : "BUY_MATERIAL", 1);
            }
            case ASSEMBLE_ITEM_REQUEST -> recordQuest(accountId, "GET_GEAR", 1);
            case BUY_SKIN_REQUEST -> recordQuest(accountId, "BUY_SKIN", 1);
            case EQUIP_ITEM_REQUEST -> recordQuest(accountId, "EQUIP_ITEM", 1);
            case UPGRADE_ITEM_REQUEST -> recordQuest(accountId, "UPGRADE_ITEM", 1);
            case REROLL_GEN_REQUEST -> recordQuest(accountId, "GEN_REROLL", 1);
            case UNLOCK_GEN_SLOT_REQUEST -> recordQuest(accountId, "GEN_SLOT", 1);
            case STAT_STONE_REQUEST -> recordQuest(accountId, "STAT_STONE", 1);
            case UPGRADE_SKILL_REQUEST -> recordQuest(accountId, "UPGRADE_SKILL", 1);
            case BUY_POTARA_SHARDS_REQUEST -> recordQuest(accountId, "BUY_SHARDS", 1);
            case ASSEMBLE_POTARA_REQUEST -> recordQuest(accountId, "ASSEMBLE_EARRING", 1);
            default -> { }
        }
    }

    public Envelope snapshotEnvelope(long accountId, int rid) throws SQLException {
        StaticData d = data.get();
        try (Connection c = db.connection()) {
            PlayerRepository.PlayerData p = PlayerRepository.load(c, accountId);
            int cap = (int) Json.num(d.balanceObj("potara"), "daily_cap", 20);
            return Envelope.newBuilder().setRequestId(rid).setPlayerDataSnapshot(
                    PlayerRepository.toProto(d, p, cap).toBuilder().setUnreadMail(MailService.unread(c, accountId)).addAllQuests(quests.entries(c, accountId))).build();
        }
    }

    /** The account's highest character (the one a shop prices and buys for): its level counts in the gold of a win. 0 when the account has none. */
    private long topCharacter(long accountId) throws SQLException {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement(
                "SELECT character_id FROM game_character WHERE account_id = ? ORDER BY level DESC, character_id LIMIT 1")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
            }
        }
    }

    private int topLevel(long accountId) throws SQLException {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement("SELECT MAX(level) FROM game_character WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Math.max(1, rs.getInt(1)) : 1;
            }
        }
    }

    private long anyCharacter(long accountId) throws SQLException {
        try (Connection c = db.connection(); PreparedStatement ps = c.prepareStatement("SELECT MIN(character_id) FROM game_character WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getLong(1) : 0;
            }
        }
    }
}
