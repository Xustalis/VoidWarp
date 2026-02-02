//! Multi-interface UDP broadcast beacon for discovery.
//!
//! On Windows, mDNS advertisements can be routed to the wrong interface (e.g. WSL, Docker).
//! This module sends a "Hello" UDP packet to the broadcast address of **every** IPv4 interface,
//! so that at least one copy reaches the same LAN as the discovering device (e.g. Android).
//! On Android we only use the UDP listener (BeaconListener); the beacon is Windows-only.

#[cfg(not(target_os = "android"))]
use local_ip_address::list_afinet_netifas;
use std::collections::HashMap;
use std::net::{IpAddr, Ipv4Addr, SocketAddr, UdpSocket};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, RwLock};
use std::thread;
use std::time::Duration;

/// Discovery beacon magic and packet type
const BEACON_MAGIC: [u8; 2] = [0x56, 0x57]; // "VW"
const PACKET_TYPE_HELLO: u8 = 0x03;

/// Multi-interface broadcast beacon: sends Hello packets on every IPv4 interface
/// so that discovery works on Windows with multiple adapters (WiFi, WSL, Docker, etc.).
/// On Android this is a no-op (we only run the UDP listener).
pub struct BroadcastBeacon {
    stop: Arc<AtomicBool>,
    handle: Option<thread::JoinHandle<()>>,
}

#[cfg(not(target_os = "android"))]
impl BroadcastBeacon {
    /// Start the beacon. Sends Hello to 255.255.255.255:port via every non-loopback IPv4 interface.
    pub fn start(device_id: String, device_name: String, port: u16) -> Self {
        let stop = Arc::new(AtomicBool::new(false));
        let stop_clone = stop.clone();

        let handle = thread::spawn(move || {
            Self::run_beacon_loop(device_id, device_name, port, stop_clone);
        });

        Self {
            stop,
            handle: Some(handle),
        }
    }

    fn run_beacon_loop(device_id: String, device_name: String, port: u16, stop: Arc<AtomicBool>) {
        let payload = build_hello_packet(&device_id, &device_name, port);
        let broadcast_addr = SocketAddr::new(IpAddr::V4(Ipv4Addr::BROADCAST), port);

        while !stop.load(Ordering::SeqCst) {
            match list_afinet_netifas() {
                Ok(interfaces) => {
                    for (name, ip) in &interfaces {
                        let IpAddr::V4(v4) = ip else { continue };
                        if v4.is_loopback() {
                            continue;
                        }
                        if let Err(e) = send_via_interface(*v4, &broadcast_addr, &payload) {
                            tracing::debug!("Beacon send via {} ({}): {}", name, v4, e);
                        } else {
                            tracing::info!(
                                "Broadcasting to {} via {} ({})",
                                broadcast_addr,
                                v4,
                                name
                            );
                        }
                    }
                }
                Err(e) => {
                    tracing::warn!("Failed to list interfaces for beacon: {}", e);
                }
            }
            for _ in 0..20 {
                if stop.load(Ordering::SeqCst) {
                    return;
                }
                thread::sleep(Duration::from_millis(100));
            }
        }
    }

    /// Stop the beacon.
    pub fn stop(mut self) {
        self.stop.store(true, Ordering::SeqCst);
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}

#[cfg(target_os = "android")]
impl BroadcastBeacon {
    /// No-op on Android (beacon is Windows-only; Android only runs the UDP listener).
    pub fn start(_device_id: String, _device_name: String, _port: u16) -> Self {
        Self {
            stop: Arc::new(AtomicBool::new(true)),
            handle: None,
        }
    }

    pub fn stop(self) {}
}

fn build_hello_packet(device_id: &str, device_name: &str, port: u16) -> Vec<u8> {
    let id_bytes = device_id.as_bytes();
    let name_bytes = device_name.as_bytes();
    let id_len = id_bytes.len().min(255) as u8;
    let name_len = name_bytes.len().min(255) as u8;
    let mut buf = Vec::with_capacity(2 + 1 + 2 + 1 + id_len as usize + 1 + name_len as usize);
    buf.extend_from_slice(&BEACON_MAGIC);
    buf.push(PACKET_TYPE_HELLO);
    buf.extend_from_slice(&port.to_be_bytes());
    buf.push(id_len);
    buf.extend_from_slice(&id_bytes[..id_len as usize]);
    buf.push(name_len);
    buf.extend_from_slice(&name_bytes[..name_len as usize]);
    buf
}

#[cfg(not(target_os = "android"))]
fn send_via_interface(
    interface_ip: Ipv4Addr,
    dest: &SocketAddr,
    payload: &[u8],
) -> std::io::Result<()> {
    let bind_addr = SocketAddr::new(IpAddr::V4(interface_ip), 0);
    let socket = UdpSocket::bind(bind_addr)?;
    socket.set_broadcast(true)?;
    socket.send_to(payload, dest)?;
    Ok(())
}

// --- UDP listener: receive Hello beacons and add peers ---

/// Parsed Hello beacon from the network
#[derive(Debug)]
pub struct HelloPeer {
    pub device_id: String,
    pub device_name: String,
    pub port: u16,
}

/// Parse a received UDP packet; returns None if not a valid Hello.
pub fn parse_hello_packet(buf: &[u8]) -> Option<HelloPeer> {
    if buf.len() < 2 + 1 + 2 + 1 {
        return None;
    }
    if buf[0] != BEACON_MAGIC[0] || buf[1] != BEACON_MAGIC[1] || buf[2] != PACKET_TYPE_HELLO {
        return None;
    }
    let port = u16::from_be_bytes([buf[3], buf[4]]);
    let mut i = 5;
    let id_len = buf.get(i)?;
    i += 1;
    let id_end = i + (*id_len as usize);
    if buf.len() < id_end + 1 {
        return None;
    }
    let device_id = String::from_utf8_lossy(&buf[i..id_end]).into_owned();
    i = id_end;
    let name_len = buf.get(i)?;
    i += 1;
    let name_end = i + (*name_len as usize);
    if buf.len() < name_end {
        return None;
    }
    let device_name = String::from_utf8_lossy(&buf[i..name_end]).into_owned();
    Some(HelloPeer {
        device_id,
        device_name,
        port,
    })
}

/// Listener that receives Hello beacons and inserts peers into the map.
pub struct BeaconListener {
    stop: Arc<AtomicBool>,
    handle: Option<thread::JoinHandle<()>>,
}

impl BeaconListener {
    /// Start listening on 0.0.0.0:port and add received peers to the map.
    pub fn start(
        port: u16,
        our_device_id: Option<String>,
        peers: Arc<RwLock<HashMap<String, super::DiscoveredPeer>>>,
    ) -> std::io::Result<Self> {
        let socket = UdpSocket::bind(SocketAddr::new(IpAddr::V4(Ipv4Addr::UNSPECIFIED), port))?;
        socket.set_broadcast(true)?;
        socket
            .set_read_timeout(Some(Duration::from_millis(500)))
            .ok();
        let stop = Arc::new(AtomicBool::new(false));
        let stop_clone = stop.clone();
        let handle = thread::spawn(move || {
            let mut buf = [0u8; 512];
            while !stop_clone.load(Ordering::SeqCst) {
                match socket.recv_from(&mut buf) {
                    Ok((len, from)) => {
                        let packet = &buf[..len];
                        if let Some(hello) = parse_hello_packet(packet) {
                            if let Some(ref our) = our_device_id {
                                if hello.device_id == *our {
                                    continue;
                                }
                            }
                            let peer = super::DiscoveredPeer {
                                device_id: hello.device_id.clone(),
                                device_name: hello.device_name,
                                addresses: vec![from.ip()],
                                port: hello.port,
                            };
                            tracing::info!(
                                "Discovered peer via UDP beacon: {} ({}) from {}",
                                peer.device_name,
                                peer.device_id,
                                from
                            );
                            let mut guard = peers.write().unwrap();
                            guard.insert(hello.device_id, peer);
                        }
                    }
                    Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock => {}
                    Err(ref e) if e.kind() == std::io::ErrorKind::TimedOut => {}
                    Err(e) => {
                        tracing::debug!("Beacon listener recv: {}", e);
                    }
                }
            }
        });
        Ok(Self {
            stop,
            handle: Some(handle),
        })
    }

    pub fn stop(mut self) {
        self.stop.store(true, Ordering::SeqCst);
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}
