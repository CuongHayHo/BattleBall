package com.battleball.server;

import com.battleball.proto.Envelope;
import com.battleball.proto.Hello;
import com.battleball.server.auth.AccountRules;
import com.battleball.server.auth.PasswordHasher;
import com.battleball.server.auth.RateLimiter;
import com.battleball.server.net.AssetInfo;
import com.battleball.server.net.ConnectionLimiter;
import com.battleball.server.net.EnvelopeCodec;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.channel.embedded.EmbeddedChannel;
import org.junit.jupiter.api.Test;

import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Fast tests without network or database. */
class UnitTests {

    @Test
    void accountRules() {
        assertTrue(AccountRules.validUsername("abcd"));
        assertTrue(AccountRules.validUsername("a_1_b_2_c_3_d_4_e_5_"));        // 20 characters
        assertFalse(AccountRules.validUsername("abc"));                          // 3 characters
        assertFalse(AccountRules.validUsername("a_1_b_2_c_3_d_4_e_5_6"));        // 21 characters
        assertFalse(AccountRules.validUsername("Abcd"));                         // upper case
        assertFalse(AccountRules.validUsername("ab cd"));
        assertFalse(AccountRules.validUsername(null));
        assertTrue(AccountRules.validPassword("12345678"));
        assertFalse(AccountRules.validPassword("1234567"));
        assertTrue(AccountRules.validPassword("x".repeat(64)));
        assertFalse(AccountRules.validPassword("x".repeat(65)));
        assertFalse(AccountRules.validPassword("ồ".repeat(22)));                 // 66 bytes in UTF-8
    }

    @Test
    void passwordHashRoundTripAndPhpCompatibility() {
        PasswordHasher h = new PasswordHasher(4);   // low cost only to keep the test fast
        String hash = h.hash("correct horse");
        assertTrue(h.verify("correct horse", hash));
        assertFalse(h.verify("wrong", hash));
        assertTrue(hash.startsWith("$2a$04$"));
        // A hash made by PHP 8.2 password_hash('secret123', PASSWORD_BCRYPT, ['cost' => 4]) uses the $2y$ prefix and must verify here.
        String fromPhp = "$2y$04$UvK45HcOaCGQyQh55xG76ODxh8tgnAjfI4.Nzj87fHB0L4J3t9L3.";
        assertTrue(h.verify("secret123", fromPhp));
        assertFalse(h.verify("secret124", fromPhp));
    }

    @Test
    void rateLimiterSlidingWindow() {
        long[] now = {0};
        RateLimiter r = new RateLimiter(3, 1000, () -> now[0]);
        assertTrue(r.allowed("k"));
        r.record("k");
        r.record("k");
        assertTrue(r.allowed("k"));
        r.record("k");
        assertFalse(r.allowed("k"));          // 3 events in the window
        now[0] = 1001;
        assertTrue(r.allowed("k"));           // the window slid past them
        r.record("k");
        r.reset("k");
        assertTrue(r.allowed("k"));
    }

    @Test
    void connectionLimiter() {
        ConnectionLimiter l = new ConnectionLimiter(2);
        assertTrue(l.tryAcquire());
        assertTrue(l.tryAcquire());
        assertFalse(l.tryAcquire());
        l.release();
        assertTrue(l.tryAcquire());
        assertEquals(2, l.current());
    }

    @Test
    void framingRoundTripLittleEndian() {
        EmbeddedChannel ch = new EmbeddedChannel(EnvelopeCodec.frameDecoder(1024), new EnvelopeCodec.Decoder(), new EnvelopeCodec.Encoder());
        Envelope msg = Envelope.newBuilder().setRequestId(7).setHello(Hello.newBuilder().setProtocolVersion(1).setClientVersion("t")).build();

        assertTrue(ch.writeOutbound(msg));
        ByteBuf wire = ch.readOutbound();
        assertEquals(msg.getSerializedSize(), wire.order(ByteOrder.LITTLE_ENDIAN).getInt(wire.readerIndex()));   // u32 LE length first

        // the same bytes delivered in two TCP pieces must give exactly one message
        ByteBuf copy = wire.copy();
        ByteBuf first = copy.readRetainedSlice(3);
        ByteBuf second = copy.readRetainedSlice(copy.readableBytes());
        assertFalse(ch.writeInbound(first));
        assertTrue(ch.writeInbound(second));
        Envelope back = ch.readInbound();
        assertEquals(msg, back);
        assertNull(ch.readInbound());
        wire.release();
    }

    @Test
    void oversizedFrameIsRefused() {
        EmbeddedChannel ch = new EmbeddedChannel(EnvelopeCodec.frameDecoder(16), new EnvelopeCodec.Decoder());
        ByteBuf big = Unpooled.buffer().order(ByteOrder.LITTLE_ENDIAN).writeInt(1000).writeZero(8);
        try {
            ch.writeInbound(big);
        } catch (Exception expected) {
            // TooLongFrameException surfaces here or through the pipeline
        }
        assertNull(ch.readInbound());
        ch.finishAndReleaseAll();
    }

    @Test
    void assetManifestVersionIsReadAndFollowsTheFile() throws Exception {
        assertEquals("", AssetInfo.version(""));
        assertEquals("", AssetInfo.version("no-such-file.json"));
        Path f = Files.createTempFile("bb-manifest", ".json");
        try {
            Files.writeString(f, "{\"version\":\"abc123\",\"packs\":[]}", StandardCharsets.UTF_8);
            assertEquals("abc123", AssetInfo.version(f.toString()));
            Files.writeString(f, "{\"version\":\"def456\",\"packs\":[]}", StandardCharsets.UTF_8);
            Files.setLastModifiedTime(f, java.nio.file.attribute.FileTime.fromMillis(System.currentTimeMillis() + 5000));
            assertEquals("def456", AssetInfo.version(f.toString()));      // a new manifest is seen without a restart
            Files.writeString(f, "not json", StandardCharsets.UTF_8);
            Files.setLastModifiedTime(f, java.nio.file.attribute.FileTime.fromMillis(System.currentTimeMillis() + 10000));
            assertEquals("", AssetInfo.version(f.toString()));            // a broken file means "no assets", never an exception
        } finally {
            Files.deleteIfExists(f);
        }
    }
}
