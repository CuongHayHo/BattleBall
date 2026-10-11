package com.battleball.server.game.match;

import com.battleball.proto.ActionError;
import com.battleball.proto.MatchStarted;
import com.battleball.proto.SkillSlot;
import com.battleball.proto.StartMatchRequest;
import com.battleball.proto.StartTrainingRequest;
import com.battleball.proto.TrainingSpot;
import com.battleball.server.data.Json;
import com.battleball.server.data.StaticData;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.db.Database;
import com.battleball.server.game.PlayerRepository;
import com.battleball.server.game.StatCalculator;

import java.security.MessageDigest;
import java.security.SecureRandom;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collections;
import java.util.HashMap;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.random.RandomGenerator;

/** Validates a StartMatchRequest, builds the squad and the boss order, records the run, and creates the engine. Blocking: use the DB executor. */
public final class MatchStarter {
    private final Database db;
    private final StaticDataStore data;
    private final SecureRandom secure = new SecureRandom();

    public MatchStarter(Database db, StaticDataStore data) {
        this.db = db;
        this.data = data;
    }

    public record Started(long runId, String token, MatchEngine engine, MatchSetup setup, long accountId, StaticData data) {
    }

    public static final class StartException extends Exception {
        public final ActionError error;

        public StartException(ActionError error, String message) {
            super(message);
            this.error = error;
        }
    }

    public Started start(long accountId, StartMatchRequest req, RandomGenerator rng) throws SQLException, StartException {
        StaticData d = data.get();
        StaticData.Dungeon dungeon = d.dungeons().get(req.getDungeonId());
        if (dungeon == null || !dungeon.enabled()) {
            throw new StartException(ActionError.NOT_FOUND, "dungeon not found");
        }
        PlayerRepository.PlayerData player;
        try (Connection c = db.connection()) {
            player = PlayerRepository.load(c, accountId);
        }
        PlayerRepository.CharacterData main = player.character(req.getControlledCharacterId());
        PlayerRepository.CharacterData partner = checkPair(d, player, main, req.getFusionPartnerId());

        // ---- the bosses of this match (docs/database/BattleBall.md 10.29): the final boss and mini-bosses always, the rest drawn without repetition
        List<MatchSetup.Encounter> normals = new ArrayList<>();
        List<MatchSetup.Encounter> minis = new ArrayList<>();
        MatchSetup.Encounter fin = null;
        List<MatchSetup.Encounter> minions = new ArrayList<>();
        for (StaticData.DungeonBoss db1 : dungeon.bosses()) {
            StaticData.Boss b = d.bosses().get(db1.bossId());
            if (b == null || !b.enabled()) {
                continue;
            }
            MatchSetup.Encounter e = new MatchSetup.Encounter(b, db1.role(), db1.mobTier());
            switch (db1.role()) {
                case "MINION" -> minions.add(e);
                case "MINI" -> minis.add(e);
                case "FINAL" -> fin = e;
                default -> normals.add(e);
            }
        }
        if (fin == null && normals.isEmpty() && minis.isEmpty()) {
            throw new StartException(ActionError.DISABLED, "this dungeon has no bosses yet");
        }
        int want = Math.max(1, dungeon.bossesPerMatch()) - (fin == null ? 0 : 1) - minis.size();
        Collections.shuffle(normals, new java.util.Random(rng.nextLong()));
        List<MatchSetup.Encounter> order = new ArrayList<>(normals.subList(0, Math.max(0, Math.min(want, normals.size()))));
        order.addAll(minis);
        if (fin != null) {
            order.add(fin);
        } else if (!order.isEmpty()) {
            MatchSetup.Encounter last = order.remove(order.size() - 1);       // no marked final boss: the last one counts as final
            order.add(new MatchSetup.Encounter(last.boss(), "FINAL", null));
        }

        // ---- the squad
        List<MatchSetup.SquadMember> squad = squad(d, player, main, partner, 0);
        MatchSetup setup0 = new MatchSetup(0, d, dungeon, squad, order, minions, main.gold(), rng);

        // ---- record the run
        byte[] raw = new byte[32];
        secure.nextBytes(raw);
        String token = Base64.getUrlEncoder().withoutPadding().encodeToString(raw);
        long runId;
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                try (PreparedStatement ps = c.prepareStatement("INSERT INTO match_run (account_id, dungeon_id, controlled_char_id, fusion_partner_id, session_token_hash) VALUES (?,?,?,?,?)",
                        Statement.RETURN_GENERATED_KEYS)) {
                    ps.setLong(1, accountId);
                    ps.setInt(2, dungeon.id());
                    ps.setLong(3, main.id());
                    ps.setObject(4, partner == null ? null : partner.id());
                    ps.setString(5, sha256(token));
                    ps.executeUpdate();
                    try (ResultSet rs = ps.getGeneratedKeys()) {
                        rs.next();
                        runId = rs.getLong(1);
                    }
                }
                int n = 1;
                for (MatchSetup.Encounter e : order) {
                    try (PreparedStatement ps = c.prepareStatement("INSERT INTO match_run_boss (run_id, order_no, boss_id, role) VALUES (?,?,?,?)")) {
                        ps.setLong(1, runId);
                        ps.setInt(2, n++);
                        ps.setInt(3, e.boss().id());
                        ps.setString(4, e.role().equals("MINION") ? "NORMAL" : e.role());
                        ps.executeUpdate();
                    }
                }
                c.commit();
            } catch (SQLException e) {
                c.rollback();
                throw e;
            }
        }
        MatchSetup setup = new MatchSetup(runId, d, dungeon, squad, order, minions, main.gold(), rng);
        MatchEngine engine = new MatchEngine(setup);
        engine.applyCooldowns(cooldownsOf(player), System.currentTimeMillis());
        return new Started(runId, token, engine, setup, accountId, d);
    }

    /** The controlled character and its fusion partner (null when none), checked as for a match. */
    private static PlayerRepository.CharacterData checkPair(StaticData d, PlayerRepository.PlayerData player, PlayerRepository.CharacterData main, long partnerId)
            throws StartException {
        if (main == null) {
            throw new StartException(ActionError.NOT_FOUND, "character not found");
        }
        if (partnerId == 0) {
            return null;
        }
        PlayerRepository.CharacterData partner = player.character(partnerId);
        int minLevel = (int) Json.num(d.balanceObj("potara"), "min_level", 50);
        if (partner == null || partner.id() == main.id()) {
            throw new StartException(ActionError.NOT_FOUND, "fusion partner not found");
        }
        if (!player.potara().owned()) {
            throw new StartException(ActionError.NOT_OWNED, "the Potara earring is needed to fuse");
        }
        if (main.level() < minLevel || partner.level() < minLevel) {
            throw new StartException(ActionError.LEVEL_TOO_LOW, "both characters need level " + minLevel + " to fuse");
        }
        return partner;
    }

    /** The squad: the controlled character (with its partner when fused) first, then the other characters as AI. */
    private static List<MatchSetup.SquadMember> squad(StaticData d, PlayerRepository.PlayerData player, PlayerRepository.CharacterData main,
                                                      PlayerRepository.CharacterData partner, int unlearnedLevel) {
        List<MatchSetup.SquadMember> squad = new ArrayList<>();
        squad.add(member(d, main, partner, true, unlearnedLevel));
        for (PlayerRepository.CharacterData ch : player.characters()) {
            if (ch.id() != main.id() && (partner == null || ch.id() != partner.id())) {
                squad.add(member(d, ch, null, false, unlearnedLevel));
            }
        }
        return squad;
    }

    /**
     * A training session of the starter village (owner decision 2026-10-08): the squad against the dummies of balance_config training placed where the client's map
     * puts them. Nothing is written to the database (no match_run, no reward); run id 0. Blocking (reads the player): use the DB executor.
     */
    public Started startTraining(long accountId, StartTrainingRequest req, RandomGenerator rng) throws SQLException, StartException {
        StaticData d = data.get();
        Map<String, Object> cfg = d.balanceObj("training");
        PlayerRepository.PlayerData player;
        try (Connection c = db.connection()) {
            player = PlayerRepository.load(c, accountId);
        }
        PlayerRepository.CharacterData main = player.character(req.getControlledCharacterId());
        PlayerRepository.CharacterData partner = checkPair(d, player, main, req.getFusionPartnerId());
        double w = Math.max(800, Math.min(40000, req.getArenaW()));
        double h = Math.max(400, Math.min(10000, req.getArenaH()));
        List<MatchSetup.Dummy> dummies = new ArrayList<>();
        if (cfg.get("dummies") instanceof List<?> list) {
            for (Object o : list) {
                if (!(o instanceof Map<?, ?> raw)) {
                    continue;
                }
                @SuppressWarnings("unchecked") Map<String, Object> m = (Map<String, Object>) raw;
                String key = String.valueOf(m.get("key"));
                for (TrainingSpot spot : req.getSpotsList()) {
                    if (spot.getKey().equals(key)) {
                        dummies.add(new MatchSetup.Dummy(key, String.valueOf(m.getOrDefault("name", key)), (long) Json.num(m, "hp", 2_000_000_000),
                                (int) Json.num(m, "mob_template_id", -1), (int) Json.num(m, "costume_id", 0), Json.num(m, "radius", 22),
                                Json.num(m, "armor_pct", 0), spot.getX(), spot.getY()));
                        break;
                    }
                }
            }
        }
        if (dummies.isEmpty()) {
            throw new StartException(ActionError.NOT_FOUND, "no training dummy at the given spots");
        }
        MatchSetup.Training training = new MatchSetup.Training(w, h, List.copyOf(dummies), (long) (Json.num(cfg, "revive_after_s", 2) * 1000),
                (long) (Json.num(cfg, "idle_end_s", 300) * 1000));
        List<MatchSetup.SquadMember> squad = squad(d, player, main, partner, (int) Json.num(cfg, "unlearned_skill_level", 1));
        MatchSetup setup = new MatchSetup(0, d, null, squad, List.of(), List.of(), main.gold(), rng, training);
        byte[] raw = new byte[32];
        secure.nextBytes(raw);
        MatchEngine engine = new MatchEngine(setup);
        engine.applyCooldowns(cooldownsOf(player), System.currentTimeMillis());
        return new Started(0, Base64.getUrlEncoder().withoutPadding().encodeToString(raw), engine, setup, accountId, d);
    }

    /** The cooldowns the characters still have from their last session (Unix ms the skill is ready), by character id. */
    private static Map<Long, Map<Integer, Long>> cooldownsOf(PlayerRepository.PlayerData player) {
        Map<Long, Map<Integer, Long>> out = new HashMap<>();
        for (PlayerRepository.CharacterData ch : player.characters()) {
            out.put(ch.id(), ch.cooldownReadyAt());
        }
        return out;
    }

    /** The MatchStarted message for a (new or returning) client. */
    public static MatchStarted toProto(Started s, MatchEngine engine, boolean fresh) {
        StaticData d = s.data();
        StaticData.Dungeon dungeon = s.setup().dungeon();
        MatchSetup.Training training = s.setup().training();
        MatchStarted.Builder b = MatchStarted.newBuilder().setRunId(s.runId()).setMatchToken(s.token()).setTickRateHz(20).setRevivesLeft(engine.revivesLeft())
                .setBossCount(engine.bossCount());
        if (training != null) {
            b.setDungeonName("Luyện tập").setTraining(true).setArena(com.battleball.proto.Arena.newBuilder().setWidth((float) training.arenaW())
                    .setHeight((float) training.arenaH()));
        } else {
            b.setDungeonId(dungeon.id()).setDungeonName(dungeon.name())
                    .setArena(com.battleball.proto.Arena.newBuilder().setWidth((float) engine.arenaWidth()).setHeight((float) engine.arenaHeight()))
                    .setTimeLimitS(dungeon.timeLimitS() == null ? 0 : dungeon.timeLimitS());
        }
        b.addAllEntities(engine.snapshotSpawns());
        b.addAllSkills(skillSlots(engine, engine.controlledFighter()));
        return b.build();
    }

    /** The skills of a fighter by slot, as the controlled character's buttons need them (levels, cooldowns left, Ki cost). */
    public static java.util.List<SkillSlot> skillSlots(MatchEngine engine, Entities.Fighter f) {
        java.util.List<SkillSlot> out = new java.util.ArrayList<>();
        if (f == null) {
            return out;
        }
        for (Map.Entry<Integer, StaticData.Skill> e : new java.util.TreeMap<>(f.skillBySlot).entrySet()) {
            StaticData.SkillLevel L = e.getValue().level(f.skillLevelBySlot.get(e.getKey()));
            if (L == null) {
                continue;
            }
            boolean all = "ALL".equals(e.getValue().kiMode());
            out.add(SkillSlot.newBuilder().setSlot(e.getKey()).setSkillId(e.getValue().id()).setLevel(L.level()).setCooldownMs(L.cooldownMs())
                    .setIconId(Math.max(0, e.getValue().iconId())).setName(e.getValue().name()).setCooldownLeftMs((int) Math.min(Integer.MAX_VALUE, engine.cooldownLeftMs(f, e.getKey())))
                    .setKiAll(all).setKiCost(all || L.kiCostPct() == null ? 0 : (int) Math.round(f.kiMax * L.kiCostPct() / 100.0)).build());
        }
        return out;
    }

    /** The figures of a fighter: the character's own, or for a fusion the sum of the main character (with its fusion skin) and its partner. */
    static StatCalculator.CharacterStats figures(StaticData d, PlayerRepository.CharacterData ch, PlayerRepository.CharacterData partner) {
        boolean fused = partner != null;
        StatCalculator.CharacterStats s = PlayerRepository.stats(d, ch, fused);
        if (fused) {
            // fusion: the stats of both characters are ADDED (CONFIRMED); buffs are applied afterwards by the engine
            StatCalculator.CharacterStats p = PlayerRepository.stats(d, partner, false);
            Map<String, Double> other = new HashMap<>(s.other());
            p.other().forEach((k, v) -> other.merge(k, v, Double::sum));
            s = new StatCalculator.CharacterStats(s.hpMax() + p.hpMax(), s.kiMax() + p.kiMax(), s.atk() + p.atk(), s.armor() + p.armor(),
                    Math.min(100.0, s.critRatePct() + p.critRatePct()), s.critDmgPct() + p.critDmgPct(), s.dodgePct() + p.dodgePct(),
                    Math.max(s.hpRegenPctPerSec(), p.hpRegenPctPerSec()), Math.max(s.kiRegenPctPerSec(), p.kiRegenPctPerSec()), other);
        }
        return s;
    }

    /** The figures every fighter of a running session should have now (character id to figures), from the player as the database holds it. */
    public static Map<Long, StatCalculator.CharacterStats> currentFigures(StaticData d, PlayerRepository.PlayerData player, MatchSetup setup) {
        Map<Long, StatCalculator.CharacterStats> out = new HashMap<>();
        for (MatchSetup.SquadMember m : setup.squad()) {
            PlayerRepository.CharacterData ch = player.character(m.characterId());
            if (ch == null) {
                continue;
            }
            PlayerRepository.CharacterData partner = m.partnerCharacterId() == 0 ? null : player.character(m.partnerCharacterId());
            if (m.partnerCharacterId() != 0 && partner == null) {
                continue;
            }
            out.put(m.characterId(), figures(d, ch, partner));
        }
        return out;
    }

    /** One member of the squad; {@code unlearnedLevel} above 0 (training) makes the skills of the race it has not learned usable at that level. */
    private static MatchSetup.SquadMember member(StaticData d, PlayerRepository.CharacterData ch, PlayerRepository.CharacterData partner, boolean controlled,
                                                 int unlearnedLevel) {
        boolean fused = partner != null;
        StatCalculator.CharacterStats s = figures(d, ch, partner);
        StaticData.PetEffect pet = null;
        PlayerRepository.EquipRef petRef = ch.equipment().get("SUPPORT_1");
        if (petRef != null && petRef.supportInstanceId() != null) {
            PlayerRepository.SupportRow row = ch.support(petRef.supportInstanceId());
            StaticData.SupportItem def = row == null ? null : d.supportItems().get(row.supportId());
            pet = def == null || !def.enabled() ? null : def.pet();
        }
        PlayerRepository.EquipRef skin = ch.equipment().get("SKIN");
        int costume = skin == null || skin.costumeId() == null ? 0 : skin.costumeId();
        StaticData.Skin worn = d.skins().get(costume);
        StaticData.SkinKind wornKind = worn == null ? null : d.skinKinds().get(worn.kindKey());
        if (costume == 0 || (wornKind != null && wornKind.requiresFusionMain())) {      // nothing worn (taken off): the race's own look
            costume = d.equippedDefaultSkinByRace().getOrDefault(ch.race(), 0);       // a fusion skin never sits in the character's own slot
        }
        PlayerRepository.EquipRef fusionSkin = ch.equipment().get("FUSION_SKIN");
        if (fused && fusionSkin != null && fusionSkin.costumeId() != null) {
            costume = fusionSkin.costumeId();                  // the fused form looks like its fusion skin (the main and partner skins do not change the look)
        }
        String name = ch.race().equals("EARTH") ? "Trái Đất" : ch.race().equals("NAMEC") ? "Namếc" : "Xayda";
        Map<Integer, Integer> skills = ch.skills();
        if (unlearnedLevel > 0) {
            skills = new HashMap<>(skills);
            for (StaticData.Skill sk : d.skills().values()) {
                boolean ours = sk.race().equals(ch.race()) || sk.race().equals("COMMON");
                if (ours && sk.enabled() && skills.getOrDefault(sk.id(), 0) <= 0) {
                    skills.put(sk.id(), Math.min(unlearnedLevel, sk.maxLevel()));
                }
            }
        }
        return new MatchSetup.SquadMember(ch.id(), fused ? name + " (hợp thể)" : name, ch.race(), ch.level(), controlled, fused, fused ? partner.id() : 0,
                s, skills, costume, pet);
    }

    static String sha256(String s) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(s.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
