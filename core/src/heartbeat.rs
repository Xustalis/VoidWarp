//! Heartbeat Module
//!
//! UDP-based heartbeat mechanism for connection stability detection.

use std::net::{SocketAddr, UdpSocket};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

/// Magic bytes for VoidWarp heartbeat packets
const HEARTBEAT_MAGIC: [u8; 2] = [0x56, 0x57]; // "VW"

/// Packet types
const PACKET_PING: u8 = 0x01;
const PACKET_PONG: u8 = 0x02;

/// Default timeout multiplier (miss this many pings = disconnected)
const TIMEOUT_MULTIPLIER: u64 = 3;

/// Heartbeat manager for maintaining connection status
pub struct HeartbeatManager {
    socket: Option<UdpSocket>,
    running: Arc<AtomicBool>,
    last_pong: Arc<AtomicU64>,
    interval_ms: u64,
    peer_addr: Option<SocketAddr>,
}

impl HeartbeatManager {
    /// Create a new heartbeat manager
    pub fn new() -> std::io::Result<Self> {
        Ok(HeartbeatManager {
            socket: None,
            running: Arc::new(AtomicBool::new(false)),
            last_pong: Arc::new(AtomicU64::new(0)),
            interval_ms: 5000, // 5 seconds default
            peer_addr: None,
        })
    }

    /// Set heartbeat interval in milliseconds
    pub fn set_interval(&mut self, ms: u64) {
        self.interval_ms = ms;
    }

    /// Start sending heartbeats to a peer
    pub fn start(&mut self, peer_addr: SocketAddr) -> std::io::Result<()> {
        if self.running.load(Ordering::SeqCst) {
            return Ok(());
        }

        // Bind to any available port
        let socket = UdpSocket::bind("0.0.0.0:0")?;
        socket.set_nonblocking(true)?;
        
        let socket_clone = socket.try_clone()?;
        self.socket = Some(socket);
        self.peer_addr = Some(peer_addr);
        self.running.store(true, Ordering::SeqCst);
        self.last_pong.store(current_timestamp_ms(), Ordering::SeqCst);

        let running = self.running.clone();
        let last_pong = self.last_pong.clone();
        let interval = self.interval_ms;

        // Sender thread
        thread::spawn(move || {
            tracing::info!("Heartbeat sender started for {}", peer_addr);
            
            while running.load(Ordering::SeqCst) {
                // Send ping
                let ping = create_ping_packet();
                if let Err(e) = socket_clone.send_to(&ping, peer_addr) {
                    tracing::warn!("Failed to send ping: {}", e);
                }

                // Check for pong responses
                let mut buf = [0u8; 16];
                match socket_clone.recv_from(&mut buf) {
                    Ok((len, from)) if len >= 11 && from == peer_addr => {
                        if buf[0] == HEARTBEAT_MAGIC[0] 
                            && buf[1] == HEARTBEAT_MAGIC[1] 
                            && buf[2] == PACKET_PONG 
                        {
                            last_pong.store(current_timestamp_ms(), Ordering::SeqCst);
                            tracing::trace!("Received pong from {}", from);
                        }
                    }
                    _ => {}
                }

                thread::sleep(Duration::from_millis(interval));
            }
            
            tracing::info!("Heartbeat sender stopped");
        });

        Ok(())
    }

    /// Stop heartbeat
    pub fn stop(&mut self) {
        self.running.store(false, Ordering::SeqCst);
        self.socket = None;
        self.peer_addr = None;
    }

    /// Check if peer is still alive (has responded within timeout)
    pub fn is_peer_alive(&self) -> bool {
        if !self.running.load(Ordering::SeqCst) {
            return false;
        }

        let last = self.last_pong.load(Ordering::SeqCst);
        let now = current_timestamp_ms();
        let timeout = self.interval_ms * TIMEOUT_MULTIPLIER;

        now - last < timeout
    }

    /// Get time since last pong in milliseconds
    pub fn time_since_last_pong(&self) -> u64 {
        let last = self.last_pong.load(Ordering::SeqCst);
        current_timestamp_ms() - last
    }

    /// Check if heartbeat is running
    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }
}

impl Drop for HeartbeatManager {
    fn drop(&mut self) {
        self.stop();
    }
}

/// Heartbeat responder - listens for pings and sends pongs
pub struct HeartbeatResponder {
    socket: Option<UdpSocket>,
    running: Arc<AtomicBool>,
    port: u16,
}

impl HeartbeatResponder {
    /// Create a responder on a specific port
    pub fn new(port: u16) -> std::io::Result<Self> {
        let socket = UdpSocket::bind(format!("0.0.0.0:{}", port))?;
        socket.set_nonblocking(true)?;
        let actual_port = socket.local_addr()?.port();

        Ok(HeartbeatResponder {
            socket: Some(socket),
            running: Arc::new(AtomicBool::new(false)),
            port: actual_port,
        })
    }

    /// Get the port number
    pub fn port(&self) -> u16 {
        self.port
    }

    /// Start responding to pings
    pub fn start(&self) -> std::io::Result<()> {
        if self.running.load(Ordering::SeqCst) {
            return Ok(());
        }

        let socket = self.socket.as_ref().unwrap().try_clone()?;
        let running = self.running.clone();
        running.store(true, Ordering::SeqCst);

        thread::spawn(move || {
            tracing::info!("Heartbeat responder started");
            let mut buf = [0u8; 16];

            while running.load(Ordering::SeqCst) {
                match socket.recv_from(&mut buf) {
                    Ok((len, from)) if len >= 11 => {
                        if buf[0] == HEARTBEAT_MAGIC[0] 
                            && buf[1] == HEARTBEAT_MAGIC[1] 
                            && buf[2] == PACKET_PING 
                        {
                            // Extract timestamp and send pong
                            let pong = create_pong_packet(&buf[3..11]);
                            if let Err(e) = socket.send_to(&pong, from) {
                                tracing::warn!("Failed to send pong to {}: {}", from, e);
                            }
                        }
                    }
                    Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(Duration::from_millis(50));
                    }
                    _ => {}
                }
            }

            tracing::info!("Heartbeat responder stopped");
        });

        Ok(())
    }

    /// Stop responding
    pub fn stop(&self) {
        self.running.store(false, Ordering::SeqCst);
    }
}

// Helper functions

fn current_timestamp_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

fn create_ping_packet() -> [u8; 11] {
    let mut packet = [0u8; 11];
    packet[0] = HEARTBEAT_MAGIC[0];
    packet[1] = HEARTBEAT_MAGIC[1];
    packet[2] = PACKET_PING;
    let ts = current_timestamp_ms().to_be_bytes();
    packet[3..11].copy_from_slice(&ts);
    packet
}

fn create_pong_packet(timestamp: &[u8]) -> [u8; 11] {
    let mut packet = [0u8; 11];
    packet[0] = HEARTBEAT_MAGIC[0];
    packet[1] = HEARTBEAT_MAGIC[1];
    packet[2] = PACKET_PONG;
    packet[3..11].copy_from_slice(&timestamp[..8]);
    packet
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_heartbeat_creation() {
        let hb = HeartbeatManager::new().unwrap();
        assert!(!hb.is_running());
    }

    #[test]
    fn test_ping_packet() {
        let ping = create_ping_packet();
        assert_eq!(ping[0], HEARTBEAT_MAGIC[0]);
        assert_eq!(ping[1], HEARTBEAT_MAGIC[1]);
        assert_eq!(ping[2], PACKET_PING);
    }
}
