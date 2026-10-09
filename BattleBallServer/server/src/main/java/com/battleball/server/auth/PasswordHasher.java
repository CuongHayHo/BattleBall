package com.battleball.server.auth;

import at.favre.lib.crypto.bcrypt.BCrypt;

import java.nio.charset.StandardCharsets;

/** bcrypt (cost 12 by decision). Hashes written here are readable by PHP password_verify, and the PHP page's $2y$ hashes are accepted here. */
public final class PasswordHasher {
    private final int cost;
    /** A real hash used to spend the same time when the username does not exist (no timing difference between the two failures). */
    private final byte[] dummyHash;

    public PasswordHasher(int cost) {
        this.cost = cost;
        this.dummyHash = BCrypt.withDefaults().hash(cost, "dummy-password-for-timing".getBytes(StandardCharsets.UTF_8));
    }

    public String hash(String password) {
        return BCrypt.withDefaults().hashToString(cost, password.toCharArray());
    }

    public boolean verify(String password, String hash) {
        return BCrypt.verifyer().verify(password.toCharArray(), hash).verified;
    }

    /** Verifies against a dummy hash to equalise timing for unknown accounts. */
    public void burn(String password) {
        BCrypt.verifyer().verify(password.toCharArray(), dummyHash);
    }
}
