package com.battleball.server.db;

import com.battleball.server.ServerConfig;
import com.zaxxer.hikari.HikariConfig;
import com.zaxxer.hikari.HikariDataSource;

import java.sql.Connection;
import java.sql.SQLException;

/** MariaDB connection pool. Used only from the DB executor, never from a Netty I/O thread or a match tick. */
public final class Database implements AutoCloseable {
    private final HikariDataSource pool;

    public Database(ServerConfig config) {
        this(config.dbUrl(), config.dbUser(), config.dbPassword(), config.dbPoolSize());
    }

    public Database(String url, String user, String password, int poolSize) {
        HikariConfig hc = new HikariConfig();
        hc.setJdbcUrl(url);
        hc.setUsername(user);
        hc.setPassword(password);
        hc.setMaximumPoolSize(poolSize);
        hc.setPoolName("battleball-db");
        hc.addDataSourceProperty("useUnicode", "true");
        this.pool = new HikariDataSource(hc);
    }

    public Connection connection() throws SQLException {
        return pool.getConnection();
    }

    @Override
    public void close() {
        pool.close();
    }
}
