package top.mossmoss.suiyi;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;

final class UiRequest {
    interface Work<T> {
        T run() throws Failure;
    }

    interface Result<T> {
        void accept(T value);
    }

    private final Handler handler = new Handler(Looper.getMainLooper());
    private Thread worker;
    private Runnable timeoutTask;
    private int generation;

    <T> void start(Work<T> work, Result<T> success, Result<Failure> failure, long timeoutMillis) {
        cancel();
        int operation = generation;
        timeoutTask = () -> {
            cancel();
            failure.accept(new Failure(R.string.request_timeout));
        };
        handler.postDelayed(timeoutTask, timeoutMillis);
        worker = new Thread(() -> {
            try {
                T value = work.run();
                handler.post(() -> complete(operation, () -> success.accept(value)));
            } catch (Failure exception) {
                handler.post(() -> complete(operation, () -> failure.accept(exception)));
            }
        }, "suiyi-request");
        worker.start();
    }

    boolean running() {
        return worker != null;
    }

    void cancel() {
        generation++;
        if (worker != null) {
            worker.interrupt();
            worker = null;
        }
        if (timeoutTask != null) {
            handler.removeCallbacks(timeoutTask);
            timeoutTask = null;
        }
    }

    private void complete(int operation, Runnable deliver) {
        if (operation != generation) {
            return;
        }
        cancel();
        deliver.run();
    }

    static class Failure extends Exception {
        private final int message;

        Failure(int message) {
            this.message = message;
        }

        String message(Context context) {
            return context.getString(message);
        }
    }
}
