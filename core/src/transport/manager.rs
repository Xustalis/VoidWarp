//! Transport Manager
//!
//! Coordinates UDP I/O, connection management, and packet reliability.

use bytes::{Bytes, BytesMut};
use std::collections::HashMap;
use std::net::SocketAddr;
use std::sync::Arc;
use tokio::sync::{mpsc, RwLock};

use super::connection::{Connection, ConnectionState};
use super::packet::{Header, Packet, PacketError, PacketType};
use super::udp::UdpTransport;

/// Events emitted by the TransportManager
#[derive(Debug)]
pub enum TransportEvent {
    /// New connection established
    Connected { conn_id: u64, remote: SocketAddr },
    /// Data received on a connection
    Data { conn_id: u64, payload: Bytes },
    /// Connection closed
    Disconnected { conn_id: u64 },
    /// Error occurred
    Error { conn_id: Option<u64>, error: String },
}

/// Configuration for the TransportManager
#[derive(Debug, Clone)]
pub struct TransportConfig {
    pub max_retries: u32,
    pub max_connections: usize,
}

impl Default for TransportConfig {
    fn default() -> Self {
        TransportConfig {
            max_retries: 5,
            max_connections: 100,
        }
    }
}

/// Main transport controller
pub struct TransportManager {
    transport: UdpTransport,
    connections: Arc<RwLock<HashMap<u64, Connection>>>,
    #[allow(dead_code)]
    config: TransportConfig,
    event_tx: mpsc::Sender<TransportEvent>,
}

impl TransportManager {
    /// Create a new TransportManager bound to the given port
    pub async fn bind(
        port: u16,
        config: TransportConfig,
    ) -> std::io::Result<(Self, mpsc::Receiver<TransportEvent>)> {
        let transport = UdpTransport::bind_dual_stack(port).await?;
        let (event_tx, event_rx) = mpsc::channel(256);

        let manager = TransportManager {
            transport,
            connections: Arc::new(RwLock::new(HashMap::new())),
            config,
            event_tx,
        };

        Ok((manager, event_rx))
    }

    /// Get or create a connection for the given remote address
    pub async fn get_or_create_connection(&self, remote: SocketAddr) -> u64 {
        let mut conns = self.connections.write().await;

        // Check if existing connection
        for (id, conn) in conns.iter() {
            if conn.remote_addr == remote {
                return *id;
            }
        }

        // Create new connection
        let conn_id = rand_conn_id();
        let conn = Connection::new(conn_id, remote);
        conns.insert(conn_id, conn);
        conn_id
    }

    /// Send a data packet to a connection
    pub async fn send_data(&self, conn_id: u64, payload: Bytes) -> Result<(), PacketError> {
        let mut conns = self.connections.write().await;
        let conn = conns.get_mut(&conn_id).ok_or(PacketError::Incomplete)?;

        let pkt_num = conn.alloc_pkt_num();
        let header = Header {
            packet_type: PacketType::Data,
            key_phase: false,
            connection_id: conn_id,
            packet_number: pkt_num,
        };

        let packet = Packet {
            header,
            payload: payload.clone(),
        };
        let mut buf = BytesMut::new();
        packet.encode(&mut buf);

        let data = buf.freeze().to_vec();
        conn.record_sent(pkt_num, data.clone());

        self.transport
            .send(&data, conn.remote_addr)
            .await
            .map_err(|_| PacketError::Incomplete)?;
        Ok(())
    }

    /// Process incoming packets (should be run in a loop)
    pub async fn recv_loop(&self) {
        let mut buf = [0u8; 65535];

        loop {
            match self.transport.recv(&mut buf).await {
                Ok((len, remote)) => {
                    let data = Bytes::copy_from_slice(&buf[..len]);
                    if let Err(e) = self.handle_packet(data, remote).await {
                        tracing::warn!("Packet handling error: {:?}", e);
                    }
                }
                Err(e) => {
                    tracing::error!("Recv error: {:?}", e);
                    break;
                }
            }
        }
    }

    async fn handle_packet(&self, data: Bytes, remote: SocketAddr) -> Result<(), PacketError> {
        let packet = Packet::decode(data)?;
        let conn_id = packet.header.connection_id;

        match packet.header.packet_type {
            PacketType::Data => {
                // Send ACK
                self.send_ack(conn_id, packet.header.packet_number, remote)
                    .await?;

                // Emit data event
                let _ = self
                    .event_tx
                    .send(TransportEvent::Data {
                        conn_id,
                        payload: packet.payload,
                    })
                    .await;
            }
            PacketType::Ack => {
                // Process ACK
                let mut conns = self.connections.write().await;
                if let Some(conn) = conns.get_mut(&conn_id) {
                    conn.acknowledge(packet.header.packet_number);
                }
            }
            PacketType::Close => {
                let mut conns = self.connections.write().await;
                if let Some(conn) = conns.get_mut(&conn_id) {
                    conn.state = ConnectionState::Closed;
                }
                let _ = self
                    .event_tx
                    .send(TransportEvent::Disconnected { conn_id })
                    .await;
            }
            _ => {
                tracing::debug!("Unhandled packet type: {:?}", packet.header.packet_type);
            }
        }

        Ok(())
    }

    async fn send_ack(
        &self,
        conn_id: u64,
        acked_pkt_num: u64,
        remote: SocketAddr,
    ) -> Result<(), PacketError> {
        let header = Header {
            packet_type: PacketType::Ack,
            key_phase: false,
            connection_id: conn_id,
            packet_number: acked_pkt_num,
        };

        let packet = Packet {
            header,
            payload: Bytes::new(),
        };
        let mut buf = BytesMut::new();
        packet.encode(&mut buf);

        self.transport
            .send(&buf.freeze(), remote)
            .await
            .map_err(|_| PacketError::Incomplete)?;
        Ok(())
    }
}

fn rand_conn_id() -> u64 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    (nanos as u64) ^ (std::process::id() as u64)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_manager_creation() {
        let (manager, _rx) = TransportManager::bind(0, TransportConfig::default())
            .await
            .expect("Failed to create manager");

        let addr = manager.transport.local_addr().unwrap();
        assert!(addr.port() > 0);
    }
}
