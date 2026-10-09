package com.battleball.server.auth;

import java.util.ArrayDeque;
import java.util.Deque;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.LongSupplier;

/** Sliding-window counter per key (an IP address or a username): at most {@code limit} events in {@code windowMs}. */
public final class RateLimiter {
    private final int limit;
    private final long windowMs;
    private final LongSupplier clock;
    private final ConcurrentHashMap<String, Deque<Long>> events = new ConcurrentHashMap<>();

    public RateLimiter(int limit, long windowMs) {
        this(limit, windowMs, System::currentTimeMillis);
    }

    /** Constructor with an injectable clock (tests). */
    public RateLimiter(int limit, long windowMs, LongSupplier clock) {
        this.limit = limit;
        this.windowMs = windowMs;
        this.clock = clock;
    }

    /** True when the key may still act (no event is recorded). */
    public boolean allowed(String key) {
        Deque<Long> q = events.get(key);
        if (q == null) {
            return true;
        }
        synchronized (q) {
            prune(q);
            return q.size() < limit;
        }
    }

    /** Records one event (for example one failed login). */
    public void record(String key) {
        Deque<Long> q = events.computeIfAbsent(key, k -> new ArrayDeque<>());
        synchronized (q) {
            prune(q);
            q.addLast(clock.getAsLong());
        }
    }

    /** Forgets the key (for example after a successful login). */
    public void reset(String key) {
        events.remove(key);
    }

    private void prune(Deque<Long> q) {
        long cutoff = clock.getAsLong() - windowMs;
        while (!q.isEmpty() && q.peekFirst() <= cutoff) {
            q.removeFirst();
        }
    }
}
