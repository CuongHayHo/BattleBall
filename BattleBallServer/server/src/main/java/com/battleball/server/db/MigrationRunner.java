package com.battleball.server.db;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/**
 * Versioned SQL migrations (files {@code V###__description.sql} applied in order, once, recorded in table {@code schema_version}).
 * Replaces running the files by hand. A changed file that was already applied is refused (checksum); a database that was built by hand
 * can be adopted with a baseline version (every file up to it is recorded as applied without running it).
 */
public final class MigrationRunner {
    private static final Logger LOG = LoggerFactory.getLogger(MigrationRunner.class);
    private static final Pattern FILE = Pattern.compile("^V(\\d+)__(.+)\\.sql$");
    private static final String BASELINE_CHECKSUM = "BASELINE";

    private final Database db;
    private final Path dir;

    public MigrationRunner(Database db, Path dir) {
        this.db = db;
        this.dir = dir;
    }

    /** Finds db/migrations next to the working directory (works from the repository root, from server/ and from a deployed folder). */
    public static Path locate(String configured) {
        if (configured != null && !configured.isBlank()) {
            return Path.of(configured);
        }
        for (String c : new String[]{"db/migrations", "../db/migrations", "../../db/migrations", "migrations"}) {
            Path p = Path.of(c);
            if (Files.isDirectory(p)) {
                return p;
            }
        }
        throw new IllegalStateException("migrations folder not found (set BB_MIGRATIONS_DIR)");
    }

    private record Migration(int version, String description, String fileName, String sql, String checksum) {
    }

    /** Applies every pending migration; returns how many were applied. {@code baseline} &gt; 0 adopts an existing hand-built database. */
    public int migrate(int baseline) throws SQLException, IOException {
        List<Migration> all = load();
        try (Connection c = db.connection()) {
            c.setAutoCommit(true);
            try (Statement st = c.createStatement()) {
                st.execute("CREATE TABLE IF NOT EXISTS schema_version (version INT UNSIGNED NOT NULL, description VARCHAR(200) NOT NULL, "
                        + "checksum VARCHAR(64) NOT NULL, applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (version)) "
                        + "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            Map<Integer, String> applied = appliedVersions(c);
            if (applied.isEmpty() && tableExists(c, "account")) {
                if (baseline <= 0) {
                    throw new IllegalStateException("the database already has tables but no schema_version rows: set BB_BASELINE to the last migration "
                            + "number that was run by hand (for example 20)");
                }
                for (Migration m : all) {
                    if (m.version <= baseline) {
                        record(c, m, BASELINE_CHECKSUM);
                        applied.put(m.version, BASELINE_CHECKSUM);
                    }
                }
                LOG.info("adopted an existing database: migrations up to V{} recorded as applied", baseline);
            }
            for (Migration m : all) {
                String sum = applied.get(m.version);
                if (sum != null && !sum.equals(BASELINE_CHECKSUM) && !sum.equals(m.checksum)) {
                    throw new IllegalStateException("migration " + m.fileName + " was changed after it was applied (checksum mismatch)");
                }
            }
            int count = 0;
            for (Migration m : all) {
                if (applied.containsKey(m.version)) {
                    continue;
                }
                LOG.info("applying {}", m.fileName);
                try (Statement st = c.createStatement()) {
                    for (String stmt : splitStatements(m.sql)) {
                        st.execute(stmt);
                    }
                } catch (SQLException e) {
                    throw new SQLException("migration " + m.fileName + " failed: " + e.getMessage(), e.getSQLState(), e.getErrorCode(), e);
                }
                record(c, m, m.checksum);
                count++;
            }
            if (count > 0) {
                LOG.info("{} migration(s) applied", count);
            }
            return count;
        }
    }

    private List<Migration> load() throws IOException {
        TreeMap<Integer, Migration> byVersion = new TreeMap<>();
        try (Stream<Path> files = Files.list(dir)) {
            for (Path p : (Iterable<Path>) files::iterator) {
                Matcher m = FILE.matcher(p.getFileName().toString());
                if (!m.matches()) {
                    continue;
                }
                int version = Integer.parseInt(m.group(1));
                String sql = Files.readString(p, StandardCharsets.UTF_8);
                if (byVersion.put(version, new Migration(version, m.group(2), p.getFileName().toString(), sql, sha256(sql))) != null) {
                    throw new IllegalStateException("two migrations have version " + version);
                }
            }
        }
        return new ArrayList<>(byVersion.values());
    }

    private static Map<Integer, String> appliedVersions(Connection c) throws SQLException {
        Map<Integer, String> out = new TreeMap<>();
        try (Statement st = c.createStatement(); ResultSet rs = st.executeQuery("SELECT version, checksum FROM schema_version")) {
            while (rs.next()) {
                out.put(rs.getInt(1), rs.getString(2));
            }
        }
        return out;
    }

    private static boolean tableExists(Connection c, String table) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ?")) {
            ps.setString(1, table);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1) > 0;
            }
        }
    }

    private static void record(Connection c, Migration m, String checksum) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("INSERT INTO schema_version (version, description, checksum) VALUES (?, ?, ?)")) {
            ps.setInt(1, m.version);
            ps.setString(2, m.description.length() > 200 ? m.description.substring(0, 200) : m.description);
            ps.setString(3, checksum);
            ps.executeUpdate();
        }
    }

    static String sha256(String s) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8)));
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    /**
     * Splits a script into statements at semicolons, ignoring semicolons inside '...' "..." `...` strings and inside -- and slash-star comments.
     * Inside quotes a backslash escapes the next character (MariaDB default) and a doubled quote is a literal quote.
     */
    static List<String> splitStatements(String sql) {
        List<String> out = new ArrayList<>();
        StringBuilder cur = new StringBuilder();
        int n = sql.length();
        int i = 0;
        while (i < n) {
            char ch = sql.charAt(i);
            if (ch == '\'' || ch == '"' || ch == '`') {
                int j = i + 1;
                while (j < n) {
                    char q = sql.charAt(j);
                    if (q == '\\' && ch != '`') {
                        j += 2;
                        continue;
                    }
                    if (q == ch) {
                        if (j + 1 < n && sql.charAt(j + 1) == ch) {
                            j += 2;
                            continue;
                        }
                        break;
                    }
                    j++;
                }
                cur.append(sql, i, Math.min(n, j + 1));
                i = j + 1;
            } else if (ch == '-' && i + 1 < n && sql.charAt(i + 1) == '-' && (i + 2 >= n || Character.isWhitespace(sql.charAt(i + 2)))) {
                while (i < n && sql.charAt(i) != '\n') {
                    i++;
                }
            } else if (ch == '#') {
                while (i < n && sql.charAt(i) != '\n') {
                    i++;
                }
            } else if (ch == '/' && i + 1 < n && sql.charAt(i + 1) == '*') {
                int end = sql.indexOf("*/", i + 2);
                i = end < 0 ? n : end + 2;
            } else if (ch == ';') {
                addStatement(out, cur);
                cur.setLength(0);
                i++;
            } else {
                cur.append(ch);
                i++;
            }
        }
        addStatement(out, cur);
        return out;
    }

    private static void addStatement(List<String> out, StringBuilder cur) {
        String s = cur.toString().strip();
        if (!s.isEmpty()) {
            out.add(s);
        }
    }
}
