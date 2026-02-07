//! VoidWarp Core Library
//! Cross-platform file transfer with mDNS discovery

pub mod discovery;
pub mod ffi;

#[cfg(target_os = "android")]
mod android;
pub mod checksum;
pub mod heartbeat;
pub mod io_utils;
pub mod protocol;
pub mod receiver;
pub mod security;
pub mod sender;
pub mod transfer;
pub mod transport;

/// Initialize the core library (logging, runtime, etc.)
pub fn init() {
    #[cfg(target_os = "android")]
    {
        // Android-specific logging initialization
        android_logger::init_once(
            android_logger::Config::default()
                .with_max_level(log::LevelFilter::Debug)
                .with_tag("VoidWarpCore"),
        );
        log::info!("VoidWarp Core Initialized (Android Logger)");
    }

    #[cfg(not(target_os = "android"))]
    {
        // Windows/Desktop logging initialization
        // `set_global_default` is idempotent - if already set, this is a no-op
        let _ = tracing::subscriber::set_global_default(
            tracing_subscriber::FmtSubscriber::builder()
                .with_max_level(tracing::Level::INFO)
                .finish(),
        );
        tracing::info!("VoidWarp Core Initialized (Tracing Subscriber)");
    }
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
