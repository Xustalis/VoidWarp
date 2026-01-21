//! VoidWarp Core Library
//!
//! This is the engine for the VoidWarp file transfer system.

pub mod discovery;
pub mod ffi;

#[cfg(target_os = "android")]
mod android;
pub mod security;
pub mod transfer;
pub mod transport;

/// Initialize the core library (logging, runtime, etc.)
pub fn init() {
    tracing::subscriber::set_global_default(
        tracing_subscriber::FmtSubscriber::builder()
            .with_max_level(tracing::Level::INFO)
            .finish(),
    )
    .expect("setting default subscriber failed");

    tracing::info!("VoidWarp Core Initialized");
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
