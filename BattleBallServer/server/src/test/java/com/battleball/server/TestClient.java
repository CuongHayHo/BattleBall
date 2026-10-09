package com.battleball.server;

import com.battleball.proto.Envelope;
import com.battleball.server.net.EnvelopeCodec;
import io.netty.bootstrap.Bootstrap;
import io.netty.channel.Channel;
import io.netty.channel.ChannelHandlerContext;
import io.netty.channel.ChannelInitializer;
import io.netty.channel.EventLoopGroup;
import io.netty.channel.MultiThreadIoEventLoopGroup;
import io.netty.channel.SimpleChannelInboundHandler;
import io.netty.channel.nio.NioIoHandler;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioSocketChannel;
import io.netty.handler.ssl.SslContext;
import io.netty.handler.ssl.SslContextBuilder;
import io.netty.handler.ssl.util.InsecureTrustManagerFactory;

import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/** A TLS client for the tests: sends envelopes and collects what the server sends back (the development certificate is self-signed, so trust is switched off here only). */
final class TestClient implements AutoCloseable {
    private static final EventLoopGroup GROUP;

    static {
        PlatformFixes.apply();
        GROUP = new MultiThreadIoEventLoopGroup(2, NioIoHandler.newFactory());
    }
    private final LinkedBlockingQueue<Envelope> inbox = new LinkedBlockingQueue<>();
    private final Channel channel;
    private volatile boolean closed;

    TestClient(int port) throws Exception {
        SslContext ssl = SslContextBuilder.forClient().trustManager(InsecureTrustManagerFactory.INSTANCE).build();
        channel = new Bootstrap().group(GROUP).channel(NioSocketChannel.class)
                .handler(new ChannelInitializer<SocketChannel>() {
                    @Override
                    protected void initChannel(SocketChannel ch) {
                        ch.pipeline().addLast(ssl.newHandler(ch.alloc(), "localhost", port))
                                .addLast(EnvelopeCodec.frameDecoder(1 << 20))
                                .addLast(new EnvelopeCodec.Decoder())
                                .addLast(new EnvelopeCodec.Encoder())
                                .addLast(new SimpleChannelInboundHandler<Envelope>() {
                                    @Override
                                    protected void channelRead0(ChannelHandlerContext ctx, Envelope msg) {
                                        inbox.add(msg);
                                    }

                                    @Override
                                    public void channelInactive(ChannelHandlerContext ctx) {
                                        closed = true;
                                    }
                                });
                    }
                }).connect("localhost", port).sync().channel();
    }

    void send(Envelope e) {
        channel.writeAndFlush(e);
    }

    /** Next message from the server, or null after {@code timeoutMs}. */
    Envelope next(long timeoutMs) throws InterruptedException {
        return inbox.poll(timeoutMs, TimeUnit.MILLISECONDS);
    }

    Envelope expect(long timeoutMs) throws InterruptedException {
        Envelope e = next(timeoutMs);
        if (e == null) {
            throw new AssertionError("no message from the server within " + timeoutMs + " ms");
        }
        return e;
    }

    /** The first message of the given kind (other messages before it, for example the player data pushed after login, are skipped). */
    Envelope expectCase(Envelope.PayloadCase kind, long timeoutMs) throws InterruptedException {
        long end = System.currentTimeMillis() + timeoutMs;
        while (true) {
            long left = end - System.currentTimeMillis();
            Envelope e = next(Math.max(1, left));
            if (e == null) {
                throw new AssertionError("no " + kind + " from the server within " + timeoutMs + " ms");
            }
            if (e.getPayloadCase() == kind) {
                return e;
            }
        }
    }

    boolean waitClosed(long timeoutMs) throws InterruptedException {
        long end = System.currentTimeMillis() + timeoutMs;
        while (!closed && System.currentTimeMillis() < end) {
            Thread.sleep(20);
        }
        return closed;
    }

    @Override
    public void close() {
        channel.close();
    }
}
