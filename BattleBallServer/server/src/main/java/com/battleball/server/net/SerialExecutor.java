package com.battleball.server.net;

import java.util.Queue;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicBoolean;

/** Runs the tasks of one connection one after another (in order) on a shared executor, so two requests of one player never overlap. */
final class SerialExecutor implements Executor {
    private final Executor delegate;
    private final Queue<Runnable> queue = new ConcurrentLinkedQueue<>();
    private final AtomicBoolean running = new AtomicBoolean();

    SerialExecutor(Executor delegate) {
        this.delegate = delegate;
    }

    @Override
    public void execute(Runnable task) {
        queue.add(task);
        schedule();
    }

    private void schedule() {
        if (!queue.isEmpty() && running.compareAndSet(false, true)) {
            try {
                delegate.execute(this::drain);
            } catch (RuntimeException e) {
                running.set(false);
                throw e;
            }
        }
    }

    private void drain() {
        try {
            Runnable r;
            while ((r = queue.poll()) != null) {
                try {
                    r.run();
                } catch (RuntimeException ignored) {
                    // a failing task must not stop the ones behind it
                }
            }
        } finally {
            running.set(false);
            schedule();
        }
    }
}
