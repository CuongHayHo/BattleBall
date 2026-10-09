package com.battleball.server.net;

import com.battleball.server.data.Json;

import java.io.IOException;
import java.io.RandomAccessFile;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * The asset manifest written by tools/build_assets.py (docs/architecture/BattleBallServer.md 15.6, 15.13). The game server announces its
 * {@code version} in HelloAck and serves the listed pack files over the game connection (AssetManifestRequest, AssetChunkRequest), the way
 * Teamobi sends its resources (cmd -74). A pack is only ever found by the id listed in the manifest, never by a name the client sends, and
 * its file must lie inside the manifest's folder. The manifest is re-read when its modification time changes, so publishing new assets needs no restart.
 */
public final class AssetInfo {
    /** One pack of the manifest: {@code file} is inside the folder of the manifest. */
    public record Pack(String id, int scale, long size, String sha256, Path file) { }

    public record Manifest(String version, List<Pack> packs) {
        public Pack find(String id) {
            for (Pack p : packs) {
                if (p.id.equals(id)) {
                    return p;
                }
            }
            return null;
        }
    }

    private record Cached(long modified, Manifest manifest) { }

    private static final Manifest NONE = new Manifest("", List.of());
    private static final ConcurrentHashMap<String, Cached> CACHE = new ConcurrentHashMap<>();

    private AssetInfo() {
    }

    /** The manifest version, or an empty string when no manifest is configured or it cannot be read. */
    public static String version(String manifestPath) {
        return manifest(manifestPath).version;
    }

    /** The manifest, or one with no version and no packs when none is configured or the file cannot be read. Never throws. */
    public static Manifest manifest(String manifestPath) {
        if (manifestPath == null || manifestPath.isBlank()) {
            return NONE;
        }
        try {
            Path p = Path.of(manifestPath);
            long modified = Files.getLastModifiedTime(p).toMillis();
            Cached c = CACHE.get(manifestPath);
            if (c != null && c.modified == modified) {
                return c.manifest;
            }
            Manifest m = parse(p);
            CACHE.put(manifestPath, new Cached(modified, m));
            return m;
        } catch (IOException | RuntimeException e) {
            return NONE;
        }
    }

    private static Manifest parse(Path manifestFile) throws IOException {
        Map<String, Object> m = Json.parseObject(Files.readString(manifestFile, StandardCharsets.UTF_8));
        Object v = m.get("version");
        String version = v instanceof String s ? s : "";
        Path dir = manifestFile.toAbsolutePath().normalize().getParent();
        List<Pack> packs = new ArrayList<>();
        if (m.get("packs") instanceof List<?> list) {
            for (Object o : list) {
                if (!(o instanceof Map<?, ?> raw)) {
                    continue;
                }
                @SuppressWarnings("unchecked")
                Map<String, Object> e = (Map<String, Object>) raw;
                if (!(e.get("id") instanceof String id) || !(e.get("url") instanceof String url) || !(e.get("sha256") instanceof String sha)) {
                    continue;
                }
                Path file = dir.resolve(url).normalize();
                if (!file.startsWith(dir) || !Files.isRegularFile(file)) {
                    continue;                                    // a pack outside the folder or missing on disk is not served
                }
                packs.add(new Pack(id, (int) Json.num(e, "scale", 0), Files.size(file), sha.toLowerCase(java.util.Locale.ROOT), file));
            }
        }
        return new Manifest(version, List.copyOf(packs));
    }

    /** At most {@code maxBytes} bytes of the pack from {@code offset} (empty at or past the end). */
    public static byte[] readChunk(Pack pack, long offset, int maxBytes) throws IOException {
        if (offset < 0 || offset >= pack.size) {
            return new byte[0];
        }
        int n = (int) Math.min(maxBytes, pack.size - offset);
        byte[] data = new byte[n];
        try (RandomAccessFile f = new RandomAccessFile(pack.file.toFile(), "r")) {
            f.seek(offset);
            f.readFully(data);
        }
        return data;
    }
}
