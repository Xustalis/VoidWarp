//! VoidWarp Core Library
//! Cross-platform file transfer with mDNS discovery

pub mod discovery;
pub mod ffi;

#[cfg(target_os = "android")]
mod android;
pub mod checksum;
pub mod heartbeat;
pub mod receiver;
pub mod security;
pub mod sender;
pub mod transfer;
pub mod transport;

/// Initialize the core library (logging, runtime, etc.)
pub fn init() {
    // `set_global_default` panics only if we `expect`. On Android the app process
    // can recreate Activities and/or call init from multiple entry points.
    // We treat "already set" as a no-op to avoid crashing the host process.
    let _ = tracing::subscriber::set_global_default(
        tracing_subscriber::FmtSubscriber::builder()
            .with_max_level(tracing::Level::INFO)
            .finish(),
    );

    tracing::info!("VoidWarp Core Initialized (logger ready)");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn it_works() {
        assert_eq!(2 + 2, 4);
    }

    #[tokio::test]
    async fn test_async_init() {
        init();
        tracing::info!("Async test running");
    }
}
