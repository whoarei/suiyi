use std::time::{Duration, Instant};
use tokio::sync::Mutex;
use tokio_util::sync::CancellationToken;

// Canceled waiting work must not reserve future slots and delay newer input.
pub async fn wait(
    last: &Mutex<Instant>,
    cooldown: Instant,
    interval: Duration,
    cancel: &CancellationToken,
) -> bool {
    let mut started = last.lock().await;
    let next = (*started + interval).max(cooldown);
    tokio::select! { biased;
        _=cancel.cancelled()=>false,
        _=tokio::time::sleep(next.saturating_duration_since(Instant::now()))=>{
            if cancel.is_cancelled() { return false; }
            *started=Instant::now();
            true
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn cancellation_does_not_reserve_a_future_slot() {
        let original = Instant::now();
        let last = Mutex::new(original);
        let token = CancellationToken::new();
        token.cancel();
        assert!(!wait(&last, original, Duration::from_secs(1), &token).await);
        assert_eq!(*last.lock().await, original);
    }
    #[tokio::test]
    async fn interval_is_recorded_only_after_waiting() {
        let original = Instant::now();
        let last = Mutex::new(original);
        let interval = Duration::from_millis(20);
        assert!(wait(&last, original, interval, &CancellationToken::new()).await);
        assert!(last.lock().await.duration_since(original) >= interval);
    }
}
