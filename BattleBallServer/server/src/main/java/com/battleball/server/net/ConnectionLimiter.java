package com.battleball.server.net;

import java.util.concurrent.atomic.AtomicInteger;

/** At most {@code max} simultaneous connections (500 by decision); the next one gets DisconnectNotice(SERVER_FULL). */
public final class ConnectionLimiter {
    private final int max;
    private final AtomicInteger current = new AtomicInteger();

    public ConnectionLimiter(int max) {
        this.max = max;
    }

    public boolean tryAcquire() {
        while (true) {
            int c = current.get();
            if (c >= max) {
                return false;
            }
            if (current.compareAndSet(c, c + 1)) {
                return true;
            }
        }
    }

    public void release() {
        current.decrementAndGet();
    }

    public int current() {
        return current.get();
    }
}
