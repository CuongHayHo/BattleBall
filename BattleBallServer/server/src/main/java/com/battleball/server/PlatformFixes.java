package com.battleball.server;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Windows only. On JDK 21 a NIO selector (so Netty) wakes itself through a Unix-domain socket created in the temp directory.
 * When that directory is an 8.3 short path (for example C:\Users\CUONGH~1\...) the connect fails with "Unable to establish loopback connection".
 * The fix is to point jdk.net.unixdomain.tmpdir at a plain path before the first selector is opened. Harmless on other systems.
 */
public final class PlatformFixes {
    private PlatformFixes() {
    }

    public static synchronized void apply() {
        if (!System.getProperty("os.name", "").toLowerCase().contains("win") || System.getProperty("jdk.net.unixdomain.tmpdir") != null) {
            return;
        }
        String tmp = System.getProperty("java.io.tmpdir", "");
        if (!tmp.contains("~")) {
            return;
        }
        try {
            Path dir = Path.of(System.getProperty("user.home"), ".battleball", "tmp");
            Files.createDirectories(dir);
            System.setProperty("jdk.net.unixdomain.tmpdir", dir.toString());
        } catch (IOException e) {
            throw new IllegalStateException("cannot prepare a temp directory for the NIO selector", e);
        }
    }
}
