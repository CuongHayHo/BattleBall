package com.battleball.server.session;

import io.netty.channel.Channel;

import java.util.concurrent.ConcurrentHashMap;

/** Which connection currently holds each account. A second login replaces the first one (the first is kicked: LOGGED_IN_ELSEWHERE). */
public final class SessionRegistry {
    private final ConcurrentHashMap<Long, Channel> byAccount = new ConcurrentHashMap<>();

    /** Registers the channel for the account and returns the channel it replaced, or null. */
    public Channel register(long accountId, Channel channel) {
        return byAccount.put(accountId, channel);
    }

    /** Removes the entry only when it still points at this channel (a newer login must not be removed by the old connection closing). */
    public void unregister(long accountId, Channel channel) {
        byAccount.remove(accountId, channel);
    }

    public int size() {
        return byAccount.size();
    }
}
