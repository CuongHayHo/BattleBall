package com.battleball.server.session;

import io.netty.channel.Channel;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

/** Which connection currently holds each account. A second login replaces the first one (the first is kicked: LOGGED_IN_ELSEWHERE). */
public final class SessionRegistry {
    /** One online player, for the server console ("who") and the login/logout log. */
    public record Online(long accountId, String username, String ip, long loginAtMs) {
    }

    private record Entry(Channel channel, Online info) {
    }

    private final ConcurrentHashMap<Long, Entry> byAccount = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<Long, String> names = new ConcurrentHashMap<>();   // last known username of an account (a match resume has no login to tell it)

    /** Registers the channel for the account and returns the channel it replaced, or null. */
    public Channel register(long accountId, String username, String ip, Channel channel) {
        if (username == null || username.isEmpty()) {
            username = names.getOrDefault(accountId, "#" + accountId);
        } else {
            names.put(accountId, username);
        }
        Entry old = byAccount.put(accountId, new Entry(channel, new Online(accountId, username, ip, System.currentTimeMillis())));
        return old == null ? null : old.channel();
    }

    /** Removes the entry only when it still points at this channel (a newer login must not be removed by the old connection closing). */
    public void unregister(long accountId, Channel channel) {
        byAccount.computeIfPresent(accountId, (id, e) -> e.channel() == channel ? null : e);
    }

    public String nameOf(long accountId) {
        return names.getOrDefault(accountId, "#" + accountId);
    }

    /** The players online now, the ones who came first at the top. */
    public List<Online> online() {
        List<Online> list = new ArrayList<>();
        byAccount.values().forEach(e -> list.add(e.info()));
        list.sort(Comparator.comparingLong(Online::loginAtMs));
        return list;
    }

    public int size() {
        return byAccount.size();
    }
}
