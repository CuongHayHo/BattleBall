package com.battleball.server.data;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.sql.SQLException;
import java.util.Map;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Holds the current {@link StaticData}. {@link #refreshIfChanged()} compares {@code static_data_version} and swaps in a new snapshot when
 * the admin page changed anything; matches in progress keep the snapshot they started with.
 */
public final class StaticDataStore implements AutoCloseable {
    private static final Logger LOG = LoggerFactory.getLogger(StaticDataStore.class);

    private final StaticDataLoader loader;
    private final AtomicReference<StaticData> current = new AtomicReference<>();
    private ScheduledExecutorService scheduler;

    public StaticDataStore(StaticDataLoader loader) throws SQLException {
        this.loader = loader;
        current.set(loader.load());
    }

    public StaticData get() {
        return current.get();
    }

    /** Reloads when any domain version differs from the snapshot; returns true when a new snapshot was installed. */
    public boolean refreshIfChanged() throws SQLException {
        Map<String, Integer> now = loader.versions();
        if (now.equals(current.get().versions())) {
            return false;
        }
        current.set(loader.load());
        LOG.info("static data reloaded, versions {}", now);
        return true;
    }

    public void startAutoRefresh(long periodSeconds) {
        scheduler = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread t = new Thread(r, "static-data-refresh");
            t.setDaemon(true);
            return t;
        });
        scheduler.scheduleWithFixedDelay(() -> {
            try {
                refreshIfChanged();
            } catch (Exception e) {
                LOG.warn("static data refresh failed: {}", e.toString());
            }
        }, periodSeconds, periodSeconds, TimeUnit.SECONDS);
    }

    @Override
    public void close() {
        if (scheduler != null) {
            scheduler.shutdownNow();
        }
    }
}
