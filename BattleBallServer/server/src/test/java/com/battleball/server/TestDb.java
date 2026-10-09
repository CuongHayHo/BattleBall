package com.battleball.server;

import com.battleball.server.db.Database;
import com.battleball.server.db.MigrationRunner;

import java.nio.file.Path;
import java.sql.Connection;
import java.sql.Statement;
import java.util.UUID;

/**
 * A throw-away MariaDB database for tests, built by the real migrations (V001...). Needs BB_TEST_DB_URL (any URL of the server, for example
 * jdbc:mariadb://127.0.0.1:3306/), BB_TEST_DB_USER and BB_TEST_DB_PASSWORD with the right to create databases.
 */
final class TestDb implements AutoCloseable {
    final String url;
    final String user;
    final String password;
    final String name;
    private final String adminUrl;

    private TestDb(String adminUrl, String user, String password, String name) {
        this.adminUrl = adminUrl;
        this.user = user;
        this.password = password;
        this.name = name;
        this.url = adminUrl + name;
    }

    static boolean configured() {
        String v = System.getenv("BB_TEST_DB_URL");
        return v != null && !v.isBlank();
    }

    static TestDb create() throws Exception {
        TestDb t = createEmpty();
        try (Database db = new Database(t.url, t.user, t.password, 2)) {
            new MigrationRunner(db, MigrationRunner.locate(null)).migrate(0);
        }
        return t;
    }

    /** A new empty database (no tables). */
    static TestDb createEmpty() throws Exception {
        String raw = System.getenv("BB_TEST_DB_URL");
        String base = raw.substring(0, raw.lastIndexOf('/') + 1);
        String user = env("BB_TEST_DB_USER", "root");
        String password = env("BB_TEST_DB_PASSWORD", "");
        String name = "bbtest_" + UUID.randomUUID().toString().replace("-", "").substring(0, 10);
        try (Database admin = new Database(base, user, password, 1); Connection c = admin.connection(); Statement st = c.createStatement()) {
            st.execute("CREATE DATABASE `" + name + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        }
        return new TestDb(base, user, password, name);
    }

    Path migrationsDir() {
        return MigrationRunner.locate(null);
    }

    @Override
    public void close() throws Exception {
        try (Database admin = new Database(adminUrl, user, password, 1); Connection c = admin.connection(); Statement st = c.createStatement()) {
            st.execute("DROP DATABASE IF EXISTS `" + name + "`");
        }
    }

    private static String env(String k, String d) {
        String v = System.getenv(k);
        return v == null ? d : v;
    }
}
