package com.battleball.server.net;

import com.battleball.server.data.Json;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Version of the asset manifest published by the web server (docs/architecture/BattleBallServer.md 15.6). The manifest is a file written by
 * tools/build_assets.py; the game server only reads its {@code version} field to tell clients in HelloAck whether they must download.
 * The file is re-read when its modification time changes, so publishing new assets needs no server restart.
 */
public final class AssetInfo {
    private record Cached(long modified, String version) { }

    private static final ConcurrentHashMap<String, Cached> CACHE = new ConcurrentHashMap<>();

    private AssetInfo() {
    }

    /** The manifest version, or an empty string when no manifest is configured or it cannot be read. */
    public static String version(String manifestPath) {
        if (manifestPath == null || manifestPath.isBlank()) {
            return "";
        }
        try {
            Path p = Path.of(manifestPath);
            long modified = Files.getLastModifiedTime(p).toMillis();
            Cached c = CACHE.get(manifestPath);
            if (c != null && c.modified == modified) {
                return c.version;
            }
            Map<String, Object> m = Json.parseObject(Files.readString(p, StandardCharsets.UTF_8));
            Object v = m.get("version");
            String version = v instanceof String s ? s : "";
            CACHE.put(manifestPath, new Cached(modified, version));
            return version;
        } catch (IOException | RuntimeException e) {
            return "";
        }
    }
}
