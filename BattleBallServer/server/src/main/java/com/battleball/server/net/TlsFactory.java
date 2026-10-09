package com.battleball.server.net;

import io.netty.handler.ssl.SslContext;
import io.netty.handler.ssl.SslContextBuilder;

import javax.net.ssl.KeyManagerFactory;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.GeneralSecurityException;
import java.security.KeyStore;

/** Builds the TLS context from a PKCS12 keystore (self-signed in development, a real certificate such as Let's Encrypt in production). */
public final class TlsFactory {
    private TlsFactory() {
    }

    public static SslContext serverContext(Path keystore, String password) throws IOException, GeneralSecurityException {
        KeyStore ks = KeyStore.getInstance("PKCS12");
        try (InputStream in = Files.newInputStream(keystore)) {
            ks.load(in, password.toCharArray());
        }
        KeyManagerFactory kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
        kmf.init(ks, password.toCharArray());
        return SslContextBuilder.forServer(kmf).protocols("TLSv1.3", "TLSv1.2").build();
    }
}
