package com.battleball.server.net;

import com.battleball.proto.Envelope;
import com.google.protobuf.InvalidProtocolBufferException;
import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.LengthFieldBasedFrameDecoder;
import io.netty.handler.codec.MessageToByteEncoder;
import io.netty.handler.codec.MessageToMessageDecoder;

import java.nio.ByteOrder;
import java.util.List;

/**
 * Wire format (docs/protocol/BattleBall.md section 11): u32 little-endian length, then one serialized {@link Envelope}.
 * The length does not count its own four bytes.
 */
public final class EnvelopeCodec {
    private EnvelopeCodec() {
    }

    /** Splits the TCP stream into frames and strips the length field. Frames over {@code maxFrameBytes} are refused and the connection is closed. */
    public static LengthFieldBasedFrameDecoder frameDecoder(int maxFrameBytes) {
        return new LengthFieldBasedFrameDecoder(ByteOrder.LITTLE_ENDIAN, maxFrameBytes, 0, 4, 0, 4, true);
    }

    public static final class Decoder extends MessageToMessageDecoder<ByteBuf> {
        @Override
        protected void decode(ChannelHandlerContext ctx, ByteBuf frame, List<Object> out) throws InvalidProtocolBufferException {
            byte[] bytes = new byte[frame.readableBytes()];
            frame.readBytes(bytes);
            out.add(Envelope.parseFrom(bytes));
        }
    }

    public static final class Encoder extends MessageToByteEncoder<Envelope> {
        @Override
        protected void encode(ChannelHandlerContext ctx, Envelope msg, ByteBuf out) {
            out.order(ByteOrder.LITTLE_ENDIAN).writeInt(msg.getSerializedSize());
            out.writeBytes(msg.toByteArray());
        }
    }
}
