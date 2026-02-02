use std::io::{self, Read, Write};
use std::net::{IpAddr, Ipv4Addr, SocketAddr, TcpListener, TcpStream};
use std::sync::{Arc, Mutex};
use std::thread::{self, JoinHandle};
use std::time::Duration;

use socket2::{Domain, Protocol, Socket, Type};

pub const MAGIC: u32 = 0xDEADBEEF;
const HEADER_LEN: usize = 9;
const MAX_PAYLOAD_LEN: u32 = 64 * 1024 * 1024;

// General packet timeout (for Ping/Pong and data packets)
pub const DEFAULT_TIMEOUT: Duration = Duration::from_secs(10);

// Handshake timeout for user interaction (Accept/Reject decisions)
// This MUST be long enough for the user to manually accept/reject a transfer
pub const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(60);

#[repr(u8)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PacketType {
    Ping = 1,
    Pong = 2,
    Offer = 3,
    Accept = 4,
    Reject = 5,
    Data = 6,
    Ack = 7,
}

impl PacketType {
    fn from_u8(value: u8) -> Option<Self> {
        match value {
            1 => Some(PacketType::Ping),
            2 => Some(PacketType::Pong),
            3 => Some(PacketType::Offer),
            4 => Some(PacketType::Accept),
            5 => Some(PacketType::Reject),
            6 => Some(PacketType::Data),
            7 => Some(PacketType::Ack),
            _ => None,
        }
    }
}

#[derive(Clone, Debug)]
pub struct PacketHeader {
    pub packet_type: PacketType,
    pub payload_len: u32,
}

#[derive(Clone, Debug)]
pub struct Packet {
    pub header: PacketHeader,
    pub payload: Vec<u8>,
}

impl Packet {
    pub fn encode(&self) -> Vec<u8> {
        let mut buf = Vec::with_capacity(HEADER_LEN + self.payload.len());
        buf.extend_from_slice(&MAGIC.to_le_bytes());
        buf.push(self.header.packet_type as u8);
        buf.extend_from_slice(&self.header.payload_len.to_le_bytes());
        buf.extend_from_slice(&self.payload);
        buf
    }
}

fn read_packet(stream: &mut TcpStream) -> io::Result<Packet> {
    let mut header_buf = [0u8; HEADER_LEN];
    stream.read_exact(&mut header_buf)?;
    let magic = u32::from_le_bytes([header_buf[0], header_buf[1], header_buf[2], header_buf[3]]);
    if magic != MAGIC {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "invalid magic"));
    }
    let packet_type = PacketType::from_u8(header_buf[4])
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidData, "invalid packet type"))?;
    let payload_len =
        u32::from_le_bytes([header_buf[5], header_buf[6], header_buf[7], header_buf[8]]);
    if payload_len > MAX_PAYLOAD_LEN {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "payload too large",
        ));
    }
    let mut payload = vec![0u8; payload_len as usize];
    if payload_len > 0 {
        stream.read_exact(&mut payload)?;
    }
    Ok(Packet {
        header: PacketHeader {
            packet_type,
            payload_len,
        },
        payload,
    })
}

fn write_packet(stream: &mut TcpStream, packet: &Packet) -> io::Result<()> {
    let buf = packet.encode();
    stream.write_all(&buf)?;
    stream.flush()
}

fn bind_with_reuse(addr: SocketAddr) -> io::Result<TcpListener> {
    let domain = match addr {
        SocketAddr::V4(_) => Domain::IPV4,
        SocketAddr::V6(_) => Domain::IPV6,
    };
    let socket = Socket::new(domain, Type::STREAM, Some(Protocol::TCP))?;
    socket.set_reuse_address(true)?;
    socket.bind(&addr.into())?;
    socket.listen(128)?;
    Ok(socket.into())
}

pub struct TransportServer {
    connections: Arc<Mutex<Vec<SocketAddr>>>,
    _accept_thread: JoinHandle<()>,
}

impl TransportServer {
    pub fn bind(addr: SocketAddr) -> io::Result<Self> {
        let listener = bind_with_reuse(addr)?;
        let connections = Arc::new(Mutex::new(Vec::new()));
        let accept_connections = connections.clone();
        let _accept_thread = thread::spawn(move || loop {
            match listener.accept() {
                Ok((mut stream, peer)) => {
                    let mut list = accept_connections.lock().unwrap();
                    if !list.contains(&peer) {
                        list.push(peer);
                    }
                    drop(list);
                    let conn_list = accept_connections.clone();
                    let _ = stream.set_read_timeout(Some(DEFAULT_TIMEOUT));
                    let _ = stream.set_write_timeout(Some(DEFAULT_TIMEOUT));
                    thread::spawn(move || {
                        handle_connection(&mut stream, peer, conn_list);
                    });
                }
                Err(_) => {
                    thread::sleep(Duration::from_millis(50));
                }
            }
        });
        Ok(TransportServer {
            connections,
            _accept_thread,
        })
    }

    pub fn active_connections(&self) -> Vec<SocketAddr> {
        self.connections.lock().unwrap().clone()
    }

    pub fn bind_default(port: u16) -> io::Result<Self> {
        let addr = SocketAddr::new(IpAddr::V4(Ipv4Addr::UNSPECIFIED), port);
        Self::bind(addr)
    }
}

fn handle_connection(
    stream: &mut TcpStream,
    peer: SocketAddr,
    connections: Arc<Mutex<Vec<SocketAddr>>>,
) {
    tracing::debug!("Transport connection handler started for {}", peer);

    while let Ok(packet) = read_packet(stream) {
        if packet.header.packet_type == PacketType::Ping {
            tracing::trace!("Received Ping from {}, sending Pong", peer);
            let pong = Packet {
                header: PacketHeader {
                    packet_type: PacketType::Pong,
                    payload_len: 0,
                },
                payload: Vec::new(),
            };
            let _ = write_packet(stream, &pong);
        } else {
            // Note: This transport layer is for Ping/Pong keep-alive only.
            // File transfers use a separate TCP connection on the FileReceiverServer port.
            // Non-Ping packets here are ignored as they're not part of the keep-alive protocol.
            tracing::trace!(
                "Received non-Ping packet type {:?} from {} on transport port, ignoring",
                packet.header.packet_type,
                peer
            );
        }
    }

    tracing::debug!("Transport connection closed for {}", peer);
    let mut list = connections.lock().unwrap();
    list.retain(|addr| *addr != peer);
}

pub struct TransportClient {
    stream: TcpStream,
}

impl TransportClient {
    pub fn connect(addr: SocketAddr, timeout: Duration) -> io::Result<Self> {
        let stream = TcpStream::connect_timeout(&addr, timeout)?;
        stream.set_read_timeout(Some(timeout))?;
        stream.set_write_timeout(Some(timeout))?;
        Ok(TransportClient { stream })
    }

    pub fn ping(&mut self) -> io::Result<bool> {
        let packet = Packet {
            header: PacketHeader {
                packet_type: PacketType::Ping,
                payload_len: 0,
            },
            payload: Vec::new(),
        };
        write_packet(&mut self.stream, &packet)?;
        let response = read_packet(&mut self.stream)?;
        Ok(response.header.packet_type == PacketType::Pong)
    }
}
