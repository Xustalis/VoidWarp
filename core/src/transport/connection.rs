//! Connection State Management
//!
//! Manages the lifecycle and state of a VWTP connection.

use std::collections::HashMap;
use std::net::SocketAddr;
use std::time::{Duration, Instant};

/// Connection state machine states
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConnectionState {
    /// Initial state, waiting for handshake
    Idle,
    /// Handshake in progress
    Handshaking,
    /// Connection established, ready for data
    Connected,
    /// Graceful shutdown initiated
    Closing,
    /// Connection terminated
    Closed,
}

/// Represents a single VWTP connection
#[derive(Debug)]
pub struct Connection {
    pub id: u64,
    pub remote_addr: SocketAddr,
    pub state: ConnectionState,

    /// Next packet number to send
    pub next_pkt_num: u64,
    /// Highest acknowledged packet number
    pub highest_acked: u64,

    /// Unacknowledged packets: pkt_num -> (sent_time, data)
    pub pending_acks: HashMap<u64, PendingPacket>,

    /// Estimated RTT in milliseconds
    pub rtt_ms: u64,

    /// Last activity timestamp
    pub last_activity: Instant,
}

#[derive(Debug, Clone)]
pub struct PendingPacket {
    pub sent_at: Instant,
    pub data: Vec<u8>,
    pub retries: u32,
}

impl Connection {
    pub fn new(id: u64, remote_addr: SocketAddr) -> Self {
        Connection {
            id,
            remote_addr,
            state: ConnectionState::Idle,
            next_pkt_num: 0,
            highest_acked: 0,
            pending_acks: HashMap::new(),
            rtt_ms: 100, // Initial estimate
            last_activity: Instant::now(),
        }
    }

    /// Allocate the next packet number
    pub fn alloc_pkt_num(&mut self) -> u64 {
        let num = self.next_pkt_num;
        self.next_pkt_num += 1;
        num
    }

    /// Record a sent packet for ACK tracking
    pub fn record_sent(&mut self, pkt_num: u64, data: Vec<u8>) {
        self.pending_acks.insert(
            pkt_num,
            PendingPacket {
                sent_at: Instant::now(),
                data,
                retries: 0,
            },
        );
        self.last_activity = Instant::now();
    }

    /// Process an ACK for a packet number
    pub fn acknowledge(&mut self, pkt_num: u64) {
        if let Some(pending) = self.pending_acks.remove(&pkt_num) {
            // Update RTT estimate (simple exponential moving average)
            let sample_rtt = pending.sent_at.elapsed().as_millis() as u64;
            self.rtt_ms = (self.rtt_ms * 7 + sample_rtt) / 8;
        }
        if pkt_num > self.highest_acked {
            self.highest_acked = pkt_num;
        }
        self.last_activity = Instant::now();
    }

    /// Get packets that need retransmission (older than 1.5x RTT)
    pub fn get_retransmit_candidates(&self) -> Vec<u64> {
        let timeout = Duration::from_millis(self.rtt_ms * 3 / 2);
        let now = Instant::now();

        self.pending_acks
            .iter()
            .filter(|(_, p)| now.duration_since(p.sent_at) > timeout)
            .map(|(pkt_num, _)| *pkt_num)
            .collect()
    }

    /// Mark a packet for retransmission (increment retry count)
    pub fn mark_retransmit(&mut self, pkt_num: u64) -> Option<Vec<u8>> {
        if let Some(pending) = self.pending_acks.get_mut(&pkt_num) {
            pending.retries += 1;
            pending.sent_at = Instant::now();
            Some(pending.data.clone())
        } else {
            None
        }
    }

    /// Check if connection is timed out (no activity for 30 seconds)
    pub fn is_timed_out(&self) -> bool {
        self.last_activity.elapsed() > Duration::from_secs(30)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_connection_lifecycle() {
        let addr: SocketAddr = "127.0.0.1:8080".parse().unwrap();
        let mut conn = Connection::new(12345, addr);

        assert_eq!(conn.state, ConnectionState::Idle);
        assert_eq!(conn.alloc_pkt_num(), 0);
        assert_eq!(conn.alloc_pkt_num(), 1);

        conn.record_sent(0, vec![1, 2, 3]);
        assert!(conn.pending_acks.contains_key(&0));

        conn.acknowledge(0);
        assert!(!conn.pending_acks.contains_key(&0));
        assert_eq!(conn.highest_acked, 0);
    }
}
