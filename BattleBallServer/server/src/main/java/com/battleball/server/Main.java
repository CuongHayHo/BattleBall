package com.battleball.server;

import com.battleball.server.auth.AccountRepository;
import com.battleball.server.auth.AuthService;
import com.battleball.server.auth.PasswordHasher;
import com.battleball.server.data.StaticDataLoader;
import com.battleball.server.data.StaticDataStore;
import com.battleball.server.game.EconomyService;
import com.battleball.server.game.PlayerActions;
import com.battleball.server.game.match.MatchManager;
import com.battleball.server.db.Database;
import com.battleball.server.db.MigrationRunner;
import com.battleball.server.net.GameServer;
import com.battleball.server.net.TlsFactory;
import io.netty.handler.ssl.SslContext;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

/** Entry point: configuration from environment variables (see ServerConfig), then the TLS game server. */
public final class Main {
    private static final Logger LOG = LoggerFactory.getLogger(Main.class);

    private Main() {
    }

    public static void main(String[] args) throws Exception {
        ServerConfig config = ServerConfig.fromEnv();
        SslContext ssl = TlsFactory.serverContext(config.tlsKeystore(), config.tlsKeystorePassword());
        Database db = new Database(config);
        if (config.migrate()) {
            new MigrationRunner(db, MigrationRunner.locate(config.migrationsDir())).migrate(config.baseline());
        }
        ExecutorService dbExecutor = Executors.newFixedThreadPool(Math.max(2, config.dbPoolSize()), r -> {
            Thread t = new Thread(r, "db-worker");
            t.setDaemon(true);
            return t;
        });
        AuthService auth = new AuthService(new AccountRepository(db), new PasswordHasher(config.bcryptCost()), config);
        StaticDataStore staticData = new StaticDataStore(new StaticDataLoader(db));
        staticData.startAutoRefresh(30);
        PlayerActions actions = new PlayerActions(db, staticData, new EconomyService(db, staticData));
        MatchManager.expireStaleRuns(db);
        MatchManager matches = new MatchManager(db, staticData, actions, dbExecutor, Math.max(2, Runtime.getRuntime().availableProcessors()), MatchManager.defaultRandom());
        GameServer server = new GameServer(config, ssl, auth, dbExecutor, actions, matches);
        server.start();
        startConsole(server);
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            LOG.info("shutting down");
            matches.close();
            server.close();
            staticData.close();
            dbExecutor.shutdown();
            try {
                dbExecutor.awaitTermination(5, TimeUnit.SECONDS);
            } catch (InterruptedException ignored) {
                Thread.currentThread().interrupt();
            }
            db.close();
        }, "shutdown"));
        Thread.currentThread().join();
    }

    /** Commands typed in the server window: "who" lists the players online, "help" lists the commands. Ends quietly when there is no console (stdin closed). */
    private static void startConsole(GameServer server) {
        Thread t = new Thread(() -> {
            try (var in = new java.io.BufferedReader(new java.io.InputStreamReader(System.in, java.nio.charset.StandardCharsets.UTF_8))) {
                for (String line; (line = in.readLine()) != null; ) {
                    switch (line.trim().toLowerCase()) {
                        case "who", "online" -> {
                            var list = server.online();
                            long now = System.currentTimeMillis();
                            StringBuilder sb = new StringBuilder("Online: ").append(list.size());
                            for (var o : list) {
                                sb.append(String.format("%n  %-20s id=%-6d ip=%-15s since %s (%s)", o.username(), o.accountId(), o.ip(),
                                        java.time.LocalTime.ofInstant(java.time.Instant.ofEpochMilli(o.loginAtMs()), java.time.ZoneId.systemDefault()).withNano(0),
                                        com.battleball.server.net.ConnectionHandler.formatDuration(now - o.loginAtMs())));
                            }
                            System.out.println(sb);
                        }
                        case "help", "?" -> System.out.println("Commands: who (players online now), help");
                        case "" -> { }
                        default -> System.out.println("Unknown command. Type help.");
                    }
                }
            } catch (java.io.IOException e) {
                LOG.debug("console closed: {}", e.toString());
            }
        }, "console");
        t.setDaemon(true);
        t.start();
    }
}
