package com.battleball.server.net;

import com.battleball.server.ServerConfig;
import com.battleball.server.auth.AuthService;
import com.battleball.server.game.PlayerActions;
import com.battleball.server.game.match.MatchManager;
import com.battleball.server.session.SessionRegistry;
import io.netty.bootstrap.ServerBootstrap;
import io.netty.channel.Channel;
import io.netty.channel.ChannelInitializer;
import io.netty.channel.ChannelOption;
import io.netty.channel.EventLoopGroup;
import io.netty.channel.MultiThreadIoEventLoopGroup;
import io.netty.channel.nio.NioIoHandler;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioServerSocketChannel;
import io.netty.handler.ssl.SslContext;
import io.netty.handler.timeout.IdleStateHandler;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * TCP + TLS front door. Pipeline per connection (docs/protocol/BattleBall.md section 11):
 * SslHandler -> IdleStateHandler (timeout) -> length-prefixed frames -> Protobuf Envelope -> ConnectionHandler.
 * Netty I/O threads only parse and dispatch; bcrypt and SQL run on {@code dbExecutor}; match ticks (later) get their own pool.
 */
public final class GameServer implements AutoCloseable {
    private static final Logger LOG = LoggerFactory.getLogger(GameServer.class);

    private final ServerConfig config;
    private final SslContext ssl;
    private final AuthService auth;
    private final SessionRegistry sessions = new SessionRegistry();
    private final ConnectionLimiter limiter;
    private final ExecutorService dbExecutor;
    private final PlayerActions actions;
    private final MatchManager matches;
    private EventLoopGroup boss;
    private EventLoopGroup workers;
    private Channel serverChannel;

    public GameServer(ServerConfig config, SslContext ssl, AuthService auth, ExecutorService dbExecutor, PlayerActions actions, MatchManager matches) {
        this.config = config;
        this.ssl = ssl;
        this.auth = auth;
        this.dbExecutor = dbExecutor;
        this.actions = actions;
        this.matches = matches;
        this.limiter = new ConnectionLimiter(config.maxConnections());
    }

    /** The players online now (for the console command "who"). */
    public java.util.List<SessionRegistry.Online> online() {
        return sessions.online();
    }

    /** Binds the port (0 = any free port) and returns the port actually used. */
    public int start() throws InterruptedException {
        com.battleball.server.PlatformFixes.apply();
        boss = new MultiThreadIoEventLoopGroup(1, NioIoHandler.newFactory());
        workers = new MultiThreadIoEventLoopGroup(Math.max(2, Runtime.getRuntime().availableProcessors()), NioIoHandler.newFactory());
        ServerBootstrap b = new ServerBootstrap()
                .group(boss, workers)
                .channel(NioServerSocketChannel.class)
                .option(ChannelOption.SO_BACKLOG, 128)
                .childOption(ChannelOption.TCP_NODELAY, true)
                .childOption(ChannelOption.SO_KEEPALIVE, true)
                .childHandler(new ChannelInitializer<SocketChannel>() {
                    @Override
                    protected void initChannel(SocketChannel ch) {
                        ch.pipeline()
                                .addLast("ssl", ssl.newHandler(ch.alloc()))
                                .addLast("idle", new IdleStateHandler(config.idleTimeoutMs(), 0, 0, TimeUnit.MILLISECONDS))
                                .addLast("frames", EnvelopeCodec.frameDecoder(config.maxFrameBytes()))
                                .addLast("decoder", new EnvelopeCodec.Decoder())
                                .addLast("encoder", new EnvelopeCodec.Encoder())
                                .addLast("handler", new ConnectionHandler(config, auth, sessions, dbExecutor, limiter, actions, matches));
                    }
                });
        serverChannel = b.bind(config.port()).sync().channel();
        int port = ((java.net.InetSocketAddress) serverChannel.localAddress()).getPort();
        LOG.info("BattleBallServer listening on TLS port {} (max {} connections)", port, config.maxConnections());
        return port;
    }

    public int connections() {
        return limiter.current();
    }

    @Override
    public void close() {
        if (serverChannel != null) {
            serverChannel.close().syncUninterruptibly();
        }
        if (workers != null) {
            workers.shutdownGracefully(0, 2, TimeUnit.SECONDS).syncUninterruptibly();
        }
        if (boss != null) {
            boss.shutdownGracefully(0, 2, TimeUnit.SECONDS).syncUninterruptibly();
        }
    }
}
