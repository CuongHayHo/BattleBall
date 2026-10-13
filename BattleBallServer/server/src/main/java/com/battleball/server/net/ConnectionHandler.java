package com.battleball.server.net;

import com.battleball.proto.AssetChunk;
import com.battleball.proto.AssetChunkRequest;
import com.battleball.proto.AssetManifestReply;
import com.battleball.proto.AssetPackInfo;
import com.battleball.proto.DisconnectNotice;
import com.battleball.proto.DisconnectReason;
import com.battleball.proto.Envelope;
import com.battleball.proto.ErrorCode;
import com.battleball.proto.ErrorMessage;
import com.battleball.proto.Hello;
import com.battleball.proto.HelloAck;
import com.battleball.proto.LoginRequest;
import com.battleball.proto.Pong;
import com.battleball.proto.RegisterRequest;
import com.battleball.server.ServerConfig;
import com.battleball.server.auth.AuthService;
import com.battleball.server.game.PlayerActions;
import com.battleball.server.game.match.MatchManager;
import com.battleball.server.session.SessionRegistry;
import io.netty.channel.Channel;
import io.netty.channel.ChannelFutureListener;
import io.netty.channel.ChannelHandlerContext;
import io.netty.channel.SimpleChannelInboundHandler;
import io.netty.handler.timeout.IdleState;
import io.netty.handler.timeout.IdleStateEvent;
import io.netty.util.AttributeKey;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.net.InetSocketAddress;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

/**
 * One instance per connection. Connection lifecycle of docs/protocol/BattleBall.md section 2:
 * CONNECTED -Hello-> HANDSHAKED -Login-> AUTHENTICATED -> CLOSED. The state is only touched on this channel's event-loop thread;
 * slow work (bcrypt, SQL) runs on the DB executor and its answer is posted back to the event loop.
 */
public final class ConnectionHandler extends SimpleChannelInboundHandler<Envelope> {
    private static final Logger LOG = LoggerFactory.getLogger(ConnectionHandler.class);
    private static final int MAX_VIOLATIONS = 3;
    /** Why a connection is closing, read by the handler of that connection for the logout log line. */
    private static final AttributeKey<String> CLOSE_REASON = AttributeKey.valueOf("bb.closeReason");
    private static final int ASSET_CHUNK_DEFAULT = 128 * 1024;
    private static final int ASSET_CHUNK_MAX = 256 * 1024;

    enum State { CONNECTED, HANDSHAKED, AUTHENTICATED, CLOSED }

    private final ServerConfig config;
    private final AuthService auth;
    private final SessionRegistry sessions;
    private final ExecutorService dbExecutor;
    private final ConnectionLimiter limiter;
    private final PlayerActions actions;
    private final MatchManager matches;
    private final SerialExecutor serial;

    private State state = State.CONNECTED;
    private boolean counted;
    private boolean busy;          // a register/login is being processed: one at a time per connection
    private int violations;
    private long accountId;
    private String ip = "?";
    private String username = "";
    private long loginAtMs;
    private ScheduledFuture<?> helloTimer;

    public ConnectionHandler(ServerConfig config, AuthService auth, SessionRegistry sessions, ExecutorService dbExecutor, ConnectionLimiter limiter,
                             PlayerActions actions, MatchManager matches) {
        this.config = config;
        this.auth = auth;
        this.sessions = sessions;
        this.dbExecutor = dbExecutor;
        this.limiter = limiter;
        this.actions = actions;
        this.matches = matches;
        this.serial = new SerialExecutor(dbExecutor);
    }

    @Override
    public void channelActive(ChannelHandlerContext ctx) {
        if (ctx.channel().remoteAddress() instanceof InetSocketAddress a) {
            ip = a.getAddress().getHostAddress();
        }
        if (!limiter.tryAcquire()) {
            sendAndClose(ctx, DisconnectReason.SERVER_FULL, "server full");
            return;
        }
        counted = true;
        helloTimer = ctx.executor().schedule(() -> {
            if (state == State.CONNECTED) {
                sendAndClose(ctx, DisconnectReason.TIMEOUT, "no Hello");
            }
        }, config.helloTimeoutMs(), TimeUnit.MILLISECONDS);
        ctx.fireChannelActive();
    }

    @Override
    public void channelInactive(ChannelHandlerContext ctx) {
        state = State.CLOSED;
        if (helloTimer != null) {
            helloTimer.cancel(false);
        }
        if (accountId != 0) {
            sessions.unregister(accountId, ctx.channel());
            String why = ctx.channel().attr(CLOSE_REASON).get();
            LOG.info("LOGOUT user={} id={} ip={} reason={} online_for={} online={}", username, accountId, ip, why == null ? "connection lost" : why,
                    formatDuration(System.currentTimeMillis() - loginAtMs), sessions.size());
            matches.onDisconnect(accountId, ctx.channel());     // a running match pauses and waits for the player to come back
        }
        if (counted) {
            limiter.release();
            counted = false;
        }
        ctx.fireChannelInactive();
    }

    @Override
    protected void channelRead0(ChannelHandlerContext ctx, Envelope msg) {
        int rid = msg.getRequestId();
        switch (msg.getPayloadCase()) {
            case HELLO -> onHello(ctx, rid, msg.getHello());
            case PING -> ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setPong(
                    Pong.newBuilder().setClientTimeMs(msg.getPing().getClientTimeMs()).setServerTimeMs(System.currentTimeMillis())).build());
            case REGISTER_REQUEST -> onRegister(ctx, rid, msg.getRegisterRequest());
            case LOGIN_REQUEST -> onLogin(ctx, rid, msg.getLoginRequest());
            case LOGOUT_REQUEST -> {
                ctx.channel().attr(CLOSE_REASON).setIfAbsent("logout");
                ctx.close();
            }
            case ASSET_MANIFEST_REQUEST -> onAssetManifest(ctx, rid);
            case ASSET_CHUNK_REQUEST -> onAssetChunk(ctx, rid, msg.getAssetChunkRequest());
            case CHANGE_PASSWORD_REQUEST -> {
                if (state != State.AUTHENTICATED) {
                    violation(ctx, rid, ErrorCode.INVALID_STATE, "log in first");
                } else {
                    long account = accountId;
                    submit(ctx, () -> {
                        var result = auth.changePassword(account, "password change", msg.getChangePasswordRequest());
                        ctx.executor().execute(() -> ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setChangePasswordResult(result).build()));
                    });
                }
            }
            case START_MATCH_REQUEST -> {
                if (state != State.AUTHENTICATED) {
                    violation(ctx, rid, ErrorCode.INVALID_STATE, "log in first");
                } else {
                    long account = accountId;
                    serial.execute(() -> matches.start(account, rid, msg.getStartMatchRequest(), ctx.channel()));
                }
            }
            case START_TRAINING_REQUEST -> {
                if (state != State.AUTHENTICATED) {
                    violation(ctx, rid, ErrorCode.INVALID_STATE, "log in first");
                } else {
                    long account = accountId;
                    serial.execute(() -> matches.startTraining(account, rid, msg.getStartTrainingRequest(), ctx.channel()));
                }
            }
            case PLAYER_STATE_REPORT -> {
                if (state == State.AUTHENTICATED) {
                    matches.report(accountId, msg.getPlayerStateReport());
                }
            }
            case USE_SKILL_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.useSkill(accountId, msg.getUseSkillRequest());
                }
            }
            case SWITCH_CONTROL_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.switchControl(accountId, msg.getSwitchControlRequest().getEntityId());
                }
            }
            case FUSE_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.fuse(accountId, msg.getRequestId(), msg.getFuseRequest().getMainCharacterId(), msg.getFuseRequest().getPartnerCharacterId());
                }
            }
            case SPLIT_FUSION_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.splitFusion(accountId, msg.getRequestId());
                }
            }
            case MATCH_READY -> {
                if (state == State.AUTHENTICATED) {
                    matches.ready(accountId);
                }
            }
            case REVIVE_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.revive(accountId);
                }
            }
            case LEAVE_MATCH_REQUEST -> {
                if (state == State.AUTHENTICATED) {
                    matches.leave(accountId);
                }
            }
            case RESUME_MATCH_REQUEST -> {
                if (state != State.HANDSHAKED || busy) {
                    violation(ctx, rid, ErrorCode.INVALID_STATE, "resume not allowed now");
                } else {
                    long account = matches.resume(rid, msg.getResumeMatchRequest().getMatchToken(), ctx.channel());
                    if (account != 0) {
                        state = State.AUTHENTICATED;
                        accountId = account;
                        username = sessions.nameOf(accountId);
                        loginAtMs = System.currentTimeMillis();
                        Channel previous = sessions.register(accountId, username, ip, ctx.channel());
                        if (previous != null && previous != ctx.channel()) {
                            kick(previous);
                        }
                        long resumed = account;                      // the client comes back without a login: it needs the player data too (it follows the MatchResumed)
                        serial.execute(() -> {
                            try {
                                Envelope snapshot = actions.snapshotEnvelope(resumed, 0);
                                ctx.executor().execute(() -> ctx.writeAndFlush(snapshot));
                            } catch (java.sql.SQLException e) {
                                LOG.error("cannot load the player data of {}", resumed, e);
                            }
                        });
                    }
                }
            }
            default -> {
                if (PlayerActions.handles(msg.getPayloadCase()) && state == State.AUTHENTICATED) {
                    long account = accountId;
                    serial.execute(() -> {
                        for (Envelope reply : actions.handle(account, msg)) {
                            ctx.executor().execute(() -> ctx.writeAndFlush(reply));
                        }
                    });
                } else {
                    violation(ctx, rid, ErrorCode.INVALID_STATE, "message not allowed here: " + msg.getPayloadCase());
                }
            }
        }
    }

    private void onHello(ChannelHandlerContext ctx, int rid, Hello hello) {
        if (state != State.CONNECTED) {
            violation(ctx, rid, ErrorCode.INVALID_STATE, "Hello already received");
            return;
        }
        if (hello.getProtocolVersion() != ServerConfig.PROTOCOL_VERSION) {
            ctx.writeAndFlush(error(rid, ErrorCode.PROTOCOL_VERSION_UNSUPPORTED, "server speaks protocol " + ServerConfig.PROTOCOL_VERSION))
                    .addListener(ChannelFutureListener.CLOSE);
            return;
        }
        state = State.HANDSHAKED;
        ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setHelloAck(HelloAck.newBuilder()
                .setProtocolVersion(ServerConfig.PROTOCOL_VERSION)
                .setServerTimeMs(System.currentTimeMillis())
                .setHeartbeatIntervalMs(config.heartbeatIntervalMs())
                .setTickRateHz(config.tickRateHz())
                .setServerName(config.serverName())
                .setAssetVersion(AssetInfo.version(config.assetManifest()))).build());
    }

    /** The list of packs (answered from memory: the manifest is cached until its file changes). Allowed once Hello is done, so before login too. */
    private void onAssetManifest(ChannelHandlerContext ctx, int rid) {
        if (state != State.HANDSHAKED && state != State.AUTHENTICATED) {
            violation(ctx, rid, ErrorCode.INVALID_STATE, "say Hello first");
            return;
        }
        AssetInfo.Manifest m = AssetInfo.manifest(config.assetManifest());
        AssetManifestReply.Builder reply = AssetManifestReply.newBuilder().setVersion(m.version());
        for (AssetInfo.Pack p : m.packs()) {
            reply.addPacks(AssetPackInfo.newBuilder().setId(p.id()).setScale(p.scale()).setSize(p.size()).setSha256(p.sha256()));
        }
        ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setAssetManifestReply(reply).build());
    }

    /** One chunk of one pack. The pack is found by its id in the manifest only; the file read runs on the executor of slow work, then the answer is posted back. */
    private void onAssetChunk(ChannelHandlerContext ctx, int rid, AssetChunkRequest req) {
        if (state != State.HANDSHAKED && state != State.AUTHENTICATED) {
            violation(ctx, rid, ErrorCode.INVALID_STATE, "say Hello first");
            return;
        }
        AssetInfo.Pack pack = AssetInfo.manifest(config.assetManifest()).find(req.getPackId());
        if (pack == null) {
            violation(ctx, rid, ErrorCode.MALFORMED, "unknown pack");
            return;
        }
        int want = req.getMaxBytes() == 0 ? ASSET_CHUNK_DEFAULT : (int) Math.min(req.getMaxBytes(), ASSET_CHUNK_MAX);
        long offset = req.getOffset();
        submit(ctx, () -> {
            byte[] data;
            try {
                data = AssetInfo.readChunk(pack, offset, want);
            } catch (java.io.IOException e) {
                LOG.warn("cannot read pack {}: {}", pack.id(), e.toString());
                ctx.executor().execute(() -> ctx.writeAndFlush(error(rid, ErrorCode.INTERNAL, "cannot read the pack")));
                return;
            }
            Envelope reply = Envelope.newBuilder().setRequestId(rid).setAssetChunk(AssetChunk.newBuilder()
                    .setPackId(pack.id()).setOffset(offset).setTotal(pack.size()).setData(com.google.protobuf.ByteString.copyFrom(data))).build();
            ctx.executor().execute(() -> ctx.writeAndFlush(reply));
        });
    }

    private void onRegister(ChannelHandlerContext ctx, int rid, RegisterRequest req) {
        if (state != State.HANDSHAKED || busy) {
            violation(ctx, rid, ErrorCode.INVALID_STATE, "register not allowed now");
            return;
        }
        busy = true;
        submit(ctx, () -> {
            var result = auth.register(ip, req);
            ctx.executor().execute(() -> {
                busy = false;
                ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setRegisterResult(result).build());
            });
        });
    }

    private void onLogin(ChannelHandlerContext ctx, int rid, LoginRequest req) {
        if (state != State.HANDSHAKED || busy) {
            violation(ctx, rid, ErrorCode.INVALID_STATE, "login not allowed now");
            return;
        }
        busy = true;
        submit(ctx, () -> {
            AuthService.LoginOutcome outcome = auth.login(ip, req);
            ctx.executor().execute(() -> {
                busy = false;
                if (state == State.CLOSED) {
                    return;
                }
                if (outcome.ok()) {
                    state = State.AUTHENTICATED;
                    accountId = outcome.accountId();
                    username = outcome.result().getUsername();
                    loginAtMs = System.currentTimeMillis();
                    Channel previous = sessions.register(accountId, username, ip, ctx.channel());
                    if (previous != null && previous != ctx.channel()) {
                        kick(previous);
                    }
                    LOG.info("LOGIN user={} id={} ip={} via={} online={}", username, accountId, ip,
                            req.getCredentialCase() == LoginRequest.CredentialCase.REFRESH_TOKEN ? "token" : "password", sessions.size());
                } else {
                    LOG.info("LOGIN FAILED user={} ip={} error={}", req.getUsername().isEmpty() ? "(token)" : printable(req.getUsername()), ip, outcome.result().getError());
                }
                ctx.writeAndFlush(Envelope.newBuilder().setRequestId(rid).setLoginResult(outcome.result()).build());
                if (outcome.ok()) {
                    long account = outcome.accountId();
                    serial.execute(() -> {
                        try {
                            Envelope snapshot = actions.snapshotEnvelope(account, 0);
                            ctx.executor().execute(() -> ctx.writeAndFlush(snapshot));
                        } catch (java.sql.SQLException e) {
                            LOG.error("cannot load the player data of {}", account, e);
                        }
                    });
                }
            });
        });
    }

    private void submit(ChannelHandlerContext ctx, Runnable work) {
        try {
            dbExecutor.execute(work);
        } catch (RejectedExecutionException e) {
            busy = false;
            sendAndClose(ctx, DisconnectReason.SERVER_SHUTDOWN, "server stopping");
        }
    }

    /** Sent to the older connection of an account that logged in again: LOGGED_IN_ELSEWHERE (CONFIRMED rule). */
    private static void kick(Channel old) {
        old.attr(CLOSE_REASON).setIfAbsent("logged in elsewhere");
        old.eventLoop().execute(() -> old.writeAndFlush(Envelope.newBuilder().setDisconnectNotice(DisconnectNotice.newBuilder()
                .setReason(DisconnectReason.LOGGED_IN_ELSEWHERE).setMessage("logged in elsewhere")).build()).addListener(ChannelFutureListener.CLOSE));
    }

    @Override
    public void userEventTriggered(ChannelHandlerContext ctx, Object evt) throws Exception {
        if (evt instanceof IdleStateEvent e && e.state() == IdleState.READER_IDLE) {
            sendAndClose(ctx, DisconnectReason.TIMEOUT, "idle");
            return;
        }
        super.userEventTriggered(ctx, evt);
    }

    @Override
    public void exceptionCaught(ChannelHandlerContext ctx, Throwable cause) {
        LOG.debug("connection {} error: {}", ip, cause.toString());
        sendAndClose(ctx, DisconnectReason.PROTOCOL_VIOLATION, "bad frame");
    }

    private void violation(ChannelHandlerContext ctx, int rid, ErrorCode code, String message) {
        ctx.writeAndFlush(error(rid, code, message));
        if (++violations >= MAX_VIOLATIONS) {
            sendAndClose(ctx, DisconnectReason.PROTOCOL_VIOLATION, "too many invalid messages");
        }
    }

    private static Envelope error(int rid, ErrorCode code, String message) {
        return Envelope.newBuilder().setRequestId(rid).setError(ErrorMessage.newBuilder().setCode(code).setMessage(message)).build();
    }

    private void sendAndClose(ChannelHandlerContext ctx, DisconnectReason reason, String message) {
        if (state == State.CLOSED || !ctx.channel().isActive()) {
            ctx.close();
            return;
        }
        ctx.channel().attr(CLOSE_REASON).setIfAbsent(reason.name().toLowerCase().replace('_', ' ') + " (" + message + ")");
        ctx.writeAndFlush(Envelope.newBuilder().setDisconnectNotice(DisconnectNotice.newBuilder().setReason(reason).setMessage(message)).build())
                .addListener(ChannelFutureListener.CLOSE);
    }

    /** A name typed by an unknown client: no control characters (a fake log line) and no more than 30 chars. */
    private static String printable(String text) {
        String t = text.length() > 30 ? text.substring(0, 30) : text;
        return t.replaceAll("\\p{Cntrl}", "?");
    }

    /** 3725000 ms -> "1h02m05s". */
    public static String formatDuration(long ms) {
        long s = Math.max(0, ms) / 1000;
        return s >= 3600 ? String.format("%dh%02dm%02ds", s / 3600, s % 3600 / 60, s % 60) : String.format("%dm%02ds", s / 60, s % 60);
    }
}
