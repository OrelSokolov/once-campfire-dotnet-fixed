//! Keep the independent golden reader schedulable when fan-out validation fills
//! the generator's task queue. This thread inherits the same generator CPU set.
use std::future::Future;
use tokio::sync::oneshot;

pub struct Control {
    stop: oneshot::Sender<()>,
    thread: std::thread::JoinHandle<()>,
}
impl Control {
    pub fn start(future: impl Future<Output = ()> + Send + 'static) -> crate::Res<Self> {
        let (stop, stopped) = oneshot::channel();
        let runtime = tokio::runtime::Builder::new_current_thread().enable_all().build()?;
        let thread = std::thread::Builder::new().name("cable-golden".into()).spawn(move || {
            runtime.block_on(async { tokio::select! { _ = stopped => {}, _ = future => {} } });
        })?;
        Ok(Self { stop, thread })
    }
    pub async fn stop(self) -> crate::Res<()> {
        let _ = self.stop.send(());
        tokio::task::spawn_blocking(move || self.thread.join())
            .await?.map_err(|_| "independent golden reader panicked")?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, atomic::{AtomicBool, Ordering}};
    #[tokio::test(flavor = "current_thread")]
    async fn control_remains_schedulable_when_the_workload_executor_is_busy_and_joins() {
        let ran = Arc::new(AtomicBool::new(false));
        let signal = ran.clone();
        let control = Control::start(async move {
            tokio::time::sleep(std::time::Duration::from_millis(5)).await;
            signal.store(true, Ordering::Release);
            std::future::pending::<()>().await;
        }).unwrap();
        std::thread::sleep(std::time::Duration::from_millis(100));
        assert!(ran.load(Ordering::Acquire));
        control.stop().await.unwrap();
    }
}
