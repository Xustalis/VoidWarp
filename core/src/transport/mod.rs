//! Transport Module
//!
//! Provides reliable UDP transport for VoidWarp.

pub mod connection;
pub mod manager;
pub mod packet;
pub mod udp;

// Re-exports for convenience
pub use connection::{Connection, ConnectionState};
pub use manager::{TransportConfig, TransportEvent, TransportManager};
pub use packet::{Header, Packet, PacketError, PacketType};
pub use udp::UdpTransport;
