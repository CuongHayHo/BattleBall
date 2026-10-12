package com.battleball.server.auth;

import com.battleball.server.db.Database;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HexFormat;
import java.util.List;
import java.util.Optional;

/** SQL for accounts and login tokens (V003 tables). Every statement is parameterised; one transaction per business operation. */
public final class AccountRepository {
    private static final String[] RACES = {"EARTH", "NAMEC", "XAYDA"};
    private static final int MYSQL_DUPLICATE_KEY = 1062;
    private final Database db;
    private final SecureRandom random = new SecureRandom();

    public AccountRepository(Database db) {
        this.db = db;
    }

    public record AccountRow(long accountId, String username, String passwordHash, boolean banned) {
    }

    public record TokenLogin(long tokenId, AccountRow account) {
    }

    public enum CreateResult { CREATED, USERNAME_TAKEN }

    /**
     * Welcome mail (V047, owner request 2026-10-11): while the admin switch welcome_mail_setting.enabled is on, a new account gets one mail with the
     * gift lines of welcome_gift. Same transaction as the account; the web page does the same in web/lib.php.
     */
    private static void sendWelcomeMail(Connection c, long accountId) throws SQLException {
        long mailId;
        try (PreparedStatement ps = c.prepareStatement(
                "INSERT INTO mail (account_id, title, body, sent_by) SELECT ?, title, body, 'system' FROM welcome_mail_setting WHERE setting_id = 1 AND enabled = 1",
                Statement.RETURN_GENERATED_KEYS)) {
            ps.setLong(1, accountId);
            if (ps.executeUpdate() == 0) {
                return;                                     // switched off
            }
            try (ResultSet keys = ps.getGeneratedKeys()) {
                keys.next();
                mailId = keys.getLong(1);
            }
        }
        try (PreparedStatement ps = c.prepareStatement(
                "INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT ?, line_no, kind, ref_id, quantity FROM welcome_gift")) {
            ps.setLong(1, mailId);
            ps.executeUpdate();
        }
    }

    /**
     * Campaign mails (V050, owner request 2026-10-12): every campaign that is on (enabled, ends_at in the future) gives a new account one mail with its gift lines; the mail
     * expires when the campaign ends. Same transaction as the account; the web page does the same in web/lib.php.
     */
    private static void sendCampaignMails(Connection c, long accountId) throws SQLException {
        List<Integer> campaigns = new ArrayList<>();
        try (PreparedStatement ps = c.prepareStatement("SELECT campaign_id FROM signup_campaign WHERE enabled = 1 AND ends_at > NOW() ORDER BY campaign_id");
             ResultSet rs = ps.executeQuery()) {
            while (rs.next()) {
                campaigns.add(rs.getInt(1));
            }
        }
        for (int campaign : campaigns) {
            long mailId;
            try (PreparedStatement ps = c.prepareStatement(
                    "INSERT INTO mail (account_id, title, body, sent_by, expires_at) SELECT ?, title, body, CONCAT('campaign:', campaign_id), ends_at FROM signup_campaign WHERE campaign_id = ?",
                    Statement.RETURN_GENERATED_KEYS)) {
                ps.setLong(1, accountId);
                ps.setInt(2, campaign);
                ps.executeUpdate();
                try (ResultSet keys = ps.getGeneratedKeys()) {
                    keys.next();
                    mailId = keys.getLong(1);
                }
            }
            try (PreparedStatement ps = c.prepareStatement(
                    "INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity) SELECT ?, line_no, kind, ref_id, quantity FROM signup_campaign_gift WHERE campaign_id = ?")) {
                ps.setLong(1, mailId);
                ps.setInt(2, campaign);
                ps.executeUpdate();
            }
        }
    }

    /**
     * Creates the account and, in the same transaction, the 3 characters (one per race, level 1), their wallets, the starter avatars
     * (race_default_skin), the equipped starter skin (docs/database/BattleBall.md 3.1 and 10.18) and the welcome mail (V047).
     */
    public CreateResult createAccount(String username, String passwordHash) throws SQLException {
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                long accountId;
                try (PreparedStatement ps = c.prepareStatement(
                        "INSERT INTO account (username, password_hash) VALUES (?, ?)", Statement.RETURN_GENERATED_KEYS)) {
                    ps.setString(1, username);
                    ps.setString(2, passwordHash);
                    ps.executeUpdate();
                    try (ResultSet keys = ps.getGeneratedKeys()) {
                        keys.next();
                        accountId = keys.getLong(1);
                    }
                }
                // the three characters share one wallet and one set of owned skins (V026)
                execute(c, "INSERT INTO account_wallet (account_id) VALUES (?)", accountId);
                sendWelcomeMail(c, accountId);
                sendCampaignMails(c, accountId);
                try (PreparedStatement ps = c.prepareStatement(
                        "INSERT IGNORE INTO owned_skin (account_id, costume_id, source) SELECT ?, costume_id, 'DEFAULT' FROM race_default_skin")) {
                    ps.setLong(1, accountId);
                    ps.executeUpdate();
                }
                for (String race : RACES) {
                    long characterId;
                    try (PreparedStatement ps = c.prepareStatement(
                            "INSERT INTO game_character (account_id, race) VALUES (?, ?)", Statement.RETURN_GENERATED_KEYS)) {
                        ps.setLong(1, accountId);
                        ps.setString(2, race);
                        ps.executeUpdate();
                        try (ResultSet keys = ps.getGeneratedKeys()) {
                            keys.next();
                            characterId = keys.getLong(1);
                        }
                    }
                    // every new character starts with the basic punch of its race at level 1 (without it a new character could not fight)
                    try (PreparedStatement ps = c.prepareStatement(
                            "INSERT INTO character_skill (character_id, skill_id, `level`) SELECT ?, skill_id, 1 FROM skill_template WHERE race = ? AND slot = 0")) {
                        ps.setLong(1, characterId);
                        ps.setString(2, race);
                        ps.executeUpdate();
                    }
                    try (PreparedStatement ps = c.prepareStatement(
                            "INSERT INTO character_equipment (character_id, slot, costume_id) SELECT ?, 'SKIN', costume_id FROM race_default_skin WHERE race = ? AND is_equipped = 1")) {
                        ps.setLong(1, characterId);
                        ps.setString(2, race);
                        ps.executeUpdate();
                    }
                }
                c.commit();
                return CreateResult.CREATED;
            } catch (SQLException e) {
                c.rollback();
                if (e.getErrorCode() == MYSQL_DUPLICATE_KEY) {
                    return CreateResult.USERNAME_TAKEN;
                }
                throw e;
            }
        }
    }

    public Optional<AccountRow> findByUsername(String username) throws SQLException {
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement("SELECT account_id, username, password_hash, banned FROM account WHERE username = ?")) {
            ps.setString(1, username);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.of(row(rs)) : Optional.empty();
            }
        }
    }

    public Optional<AccountRow> findById(long accountId) throws SQLException {
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement("SELECT account_id, username, password_hash, banned FROM account WHERE account_id = ?")) {
            ps.setLong(1, accountId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.of(row(rs)) : Optional.empty();
            }
        }
    }

    /** Sets a new password hash and revokes every refresh token of the account (the caller then issues a fresh one for the current device). */
    public void changePassword(long accountId, String passwordHash) throws SQLException {
        try (Connection c = db.connection()) {
            c.setAutoCommit(false);
            try {
                try (PreparedStatement ps = c.prepareStatement("UPDATE account SET password_hash = ? WHERE account_id = ?")) {
                    ps.setString(1, passwordHash);
                    ps.setLong(2, accountId);
                    ps.executeUpdate();
                }
                execute(c, "UPDATE auth_token SET revoked_at = NOW() WHERE account_id = ? AND revoked_at IS NULL", accountId);
                c.commit();
            } catch (SQLException e) {
                c.rollback();
                throw e;
            }
        }
    }

    public void touchLogin(long accountId) throws SQLException {
        try (Connection c = db.connection()) {
            execute(c, "UPDATE account SET last_login_at = NOW() WHERE account_id = ?", accountId);
        }
    }

    /** Creates a refresh token (sliding {@code days} days, one per device). Only the SHA-256 hash is stored; the raw token is returned once. */
    public String createRefreshToken(long accountId, String deviceInfo, int days) throws SQLException {
        byte[] raw = new byte[32];
        random.nextBytes(raw);
        String token = Base64.getUrlEncoder().withoutPadding().encodeToString(raw);
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement(
                     "INSERT INTO auth_token (account_id, token_hash, device_info, expires_at) VALUES (?, ?, ?, DATE_ADD(NOW(), INTERVAL ? DAY))")) {
            ps.setLong(1, accountId);
            ps.setString(2, sha256Hex(token));
            ps.setString(3, deviceInfo == null || deviceInfo.length() <= 160 ? deviceInfo : deviceInfo.substring(0, 160));
            ps.setInt(4, days);
            ps.executeUpdate();
        }
        return token;
    }

    public enum TokenState { VALID, EXPIRED, REVOKED, UNKNOWN }

    public record TokenCheck(TokenState state, TokenLogin login) {
    }

    public TokenCheck checkRefreshToken(String token) throws SQLException {
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement(
                     "SELECT t.token_id, t.revoked_at IS NOT NULL AS revoked, t.expires_at <= NOW() AS expired, a.account_id, a.username, a.password_hash, a.banned "
                             + "FROM auth_token t JOIN account a ON a.account_id = t.account_id WHERE t.token_hash = ?")) {
            ps.setString(1, sha256Hex(token));
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    return new TokenCheck(TokenState.UNKNOWN, null);
                }
                AccountRow account = new AccountRow(rs.getLong("account_id"), rs.getString("username"), rs.getString("password_hash"), rs.getBoolean("banned"));
                TokenLogin login = new TokenLogin(rs.getLong("token_id"), account);
                if (rs.getBoolean("revoked")) {
                    return new TokenCheck(TokenState.REVOKED, login);
                }
                if (rs.getBoolean("expired")) {
                    return new TokenCheck(TokenState.EXPIRED, login);
                }
                return new TokenCheck(TokenState.VALID, login);
            }
        }
    }

    /** Sliding expiry: every use extends the token by {@code days} days. */
    public void slideToken(long tokenId, int days) throws SQLException {
        try (Connection c = db.connection();
             PreparedStatement ps = c.prepareStatement("UPDATE auth_token SET expires_at = DATE_ADD(NOW(), INTERVAL ? DAY) WHERE token_id = ?")) {
            ps.setInt(1, days);
            ps.setLong(2, tokenId);
            ps.executeUpdate();
        }
    }

    private static AccountRow row(ResultSet rs) throws SQLException {
        return new AccountRow(rs.getLong("account_id"), rs.getString("username"), rs.getString("password_hash"), rs.getBoolean("banned"));
    }

    private static void execute(Connection c, String sql, long param) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement(sql)) {
            ps.setLong(1, param);
            ps.executeUpdate();
        }
    }

    static String sha256Hex(String s) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(s.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
