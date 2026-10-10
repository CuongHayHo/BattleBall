package com.battleball.server;

import com.battleball.server.db.Database;
import com.battleball.server.db.MigrationRunner;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.ResultSet;
import java.sql.Statement;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

@EnabledIfEnvironmentVariable(named = "BB_TEST_DB_URL", matches = ".+")
class MigrationRunnerTest {

    private static long count(Connection c, String sql) throws Exception {
        try (Statement st = c.createStatement(); ResultSet rs = st.executeQuery(sql)) {
            rs.next();
            return rs.getLong(1);
        }
    }

    @Test
    void fullChainBuildsTheSchemaAndSecondRunDoesNothing() throws Exception {
        try (TestDb t = TestDb.create(); Database db = new Database(t.url, t.user, t.password, 2)) {
            try (Connection c = db.connection()) {
                assertTrue(count(c, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()") >= 40);
                assertEquals(353, count(c, "SELECT COUNT(*) FROM skin_template"));
                long files;
                try (var s = Files.list(t.migrationsDir())) {
                    files = s.filter(p -> p.getFileName().toString().matches("V\\d+__.+\\.sql")).count();
                }
                assertEquals(files, count(c, "SELECT COUNT(*) FROM schema_version"));
            }
            assertEquals(0, new MigrationRunner(db, t.migrationsDir()).migrate(0));
        }
    }

    @Test
    void changedFileAfterApplyingIsRefused(@TempDir Path dir) throws Exception {
        Files.writeString(dir.resolve("V001__one.sql"), "CREATE TABLE t_one (id INT PRIMARY KEY);\nINSERT INTO t_one VALUES (1);\n");
        try (TestDb t = TestDb.createEmpty(); Database db = new Database(t.url, t.user, t.password, 2)) {
            assertEquals(1, new MigrationRunner(db, dir).migrate(0));
            Files.writeString(dir.resolve("V001__one.sql"), "CREATE TABLE t_one (id INT PRIMARY KEY, extra INT);\n");
            assertThrows(IllegalStateException.class, () -> new MigrationRunner(db, dir).migrate(0));
        }
    }

    @Test
    void handBuiltDatabaseNeedsABaselineThenOnlyNewFilesRun(@TempDir Path dir) throws Exception {
        Files.writeString(dir.resolve("V001__one.sql"), "CREATE TABLE account (id INT PRIMARY KEY);\n");
        Files.writeString(dir.resolve("V002__two.sql"), "CREATE TABLE t_two (id INT PRIMARY KEY);\n");
        try (TestDb t = TestDb.createEmpty(); Database db = new Database(t.url, t.user, t.password, 2)) {
            try (Connection c = db.connection(); Statement st = c.createStatement()) {
                st.execute("CREATE TABLE account (id INT PRIMARY KEY)");   // as if V001 was run by hand
            }
            assertThrows(IllegalStateException.class, () -> new MigrationRunner(db, dir).migrate(0));
            assertEquals(1, new MigrationRunner(db, dir).migrate(1));       // baseline 1: only V002 runs
            try (Connection c = db.connection()) {
                assertEquals(1, count(c, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 't_two'"));
            }
        }
    }

    @Test
    void statementSplitterRespectsQuotesAndComments(@TempDir Path dir) throws Exception {
        Files.writeString(dir.resolve("V001__quotes.sql"),
                "-- a comment; with a semicolon\n"
                        + "CREATE TABLE q (id INT PRIMARY KEY, txt VARCHAR(100));\n"
                        + "INSERT INTO q VALUES (1, 'a;b -- not a comment'), (2, 'it''s; fine'), (3, 'back\\\\slash;');\n"
                        + "/* block; comment */ INSERT INTO q VALUES (4, \"dq;text\");\n");
        try (TestDb t = TestDb.createEmpty(); Database db = new Database(t.url, t.user, t.password, 2)) {
            new MigrationRunner(db, dir).migrate(0);
            try (Connection c = db.connection()) {
                assertEquals(4, count(c, "SELECT COUNT(*) FROM q"));
                assertEquals(1, count(c, "SELECT COUNT(*) FROM q WHERE txt = 'a;b -- not a comment'"));
                assertEquals(1, count(c, "SELECT COUNT(*) FROM q WHERE txt = 'it''s; fine'"));
                assertEquals(1, count(c, "SELECT COUNT(*) FROM q WHERE txt = 'dq;text'"));
            }
        }
    }
}
