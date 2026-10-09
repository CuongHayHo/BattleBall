package com.battleball.server;

import java.nio.file.Path;

/**
 * Settings of the server. Read from environment variables; everything has a safe default except the TLS keystore.
 * Decisions: docs/networking/BattleBall.md (500 connections, TLS from the start), docs/protocol/BattleBall.md (heartbeat, tick rate).
 */
public record ServerConfig(
        int port,
        String dbUrl,
        String dbUser,
        String dbPassword,
        int dbPoolSize,
        Path tlsKeystore,
        String tlsKeystorePassword,
        int maxConnections,
        int helloTimeoutMs,
        int idleTimeoutMs,
        int heartbeatIntervalMs,
        int tickRateHz,
        int maxFrameBytes,
        int bcryptCost,
        int refreshTokenDays,
        boolean migrate,
        String migrationsDir,
        int baseline,
        String assetBaseUrl,
        String assetManifest,
        String serverName) {

    public static final int PROTOCOL_VERSION = 1;

    public static ServerConfig fromEnv() {
        String keystore = env("BB_TLS_KEYSTORE", null);
        if (keystore == null || keystore.isBlank()) {
            throw new IllegalStateException("BB_TLS_KEYSTORE is not set: the server only accepts TLS connections (make one with tools/make_dev_keystore.ps1)");
        }
        return new ServerConfig(
                intEnv("BB_PORT", 7443),
                env("BB_DB_URL", "jdbc:mariadb://127.0.0.1:3306/battleball"),
                env("BB_DB_USER", "root"),
                env("BB_DB_PASSWORD", ""),
                intEnv("BB_DB_POOL", 10),
                Path.of(keystore),
                env("BB_TLS_PASSWORD", "changeit"),
                intEnv("BB_MAX_CONNECTIONS", 500),
                intEnv("BB_HELLO_TIMEOUT_MS", 10_000),
                intEnv("BB_IDLE_TIMEOUT_MS", 60_000),
                intEnv("BB_HEARTBEAT_MS", 15_000),
                20,
                intEnv("BB_MAX_FRAME", 1 << 20),
                12,
                30,
                !"0".equals(env("BB_MIGRATE", "1")),
                env("BB_MIGRATIONS_DIR", null),
                intEnv("BB_BASELINE", 0),
                env("BB_ASSET_BASE_URL", ""),
                env("BB_ASSET_MANIFEST", ""),
                env("BB_SERVER_NAME", "BattleBall"));
    }

    private static String env(String key, String fallback) {
        String v = System.getenv(key);
        return v == null ? fallback : v;
    }

    private static int intEnv(String key, int fallback) {
        String v = System.getenv(key);
        return v == null || v.isBlank() ? fallback : Integer.parseInt(v.trim());
    }
}
