package com.battleball.server.auth;

import java.nio.charset.StandardCharsets;
import java.util.regex.Pattern;

/**
 * Account rules shared with the PHP registration page (one specification for both doors into the database, docs/database/BattleBall.md 3.1):
 * username 4 to 20 characters a-z 0-9 _ ; password 8 to 64 bytes (bcrypt reads at most 72 bytes).
 */
public final class AccountRules {
    private static final Pattern USERNAME = Pattern.compile("^[a-z0-9_]{4,20}$");
    public static final int PASSWORD_MIN_BYTES = 8;
    public static final int PASSWORD_MAX_BYTES = 64;

    private AccountRules() {
    }

    public static boolean validUsername(String username) {
        return username != null && USERNAME.matcher(username).matches();
    }

    public static boolean validPassword(String password) {
        if (password == null) {
            return false;
        }
        int n = password.getBytes(StandardCharsets.UTF_8).length;
        return n >= PASSWORD_MIN_BYTES && n <= PASSWORD_MAX_BYTES;
    }
}
