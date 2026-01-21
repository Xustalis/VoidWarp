//! Discovery Module
//!
//! mDNS-based service discovery for finding VoidWarp peers on LAN.

use mdns_sd::{Receiver as MdnsReceiver, ServiceDaemon, ServiceEvent, ServiceInfo};
use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::{Arc, RwLock};
use tokio::sync::mpsc;

/// Service type for VoidWarp
pub const SERVICE_TYPE: &str = "_voidwarp._udp.local.";

/// Discovered peer information
#[derive(Debug, Clone)]
pub struct DiscoveredPeer {
    pub device_id: String,
    pub device_name: String,
    pub addresses: Vec<IpAddr>,
    pub port: u16,
}

/// Events from the discovery system
#[derive(Debug)]
pub enum DiscoveryEvent {
    PeerFound(DiscoveredPeer),
    PeerLost(String), // device_id
}

/// Discovery Manager for mDNS operations
pub struct DiscoveryManager {
    daemon: ServiceDaemon,
    peers: Arc<RwLock<HashMap<String, DiscoveredPeer>>>,
    our_service: Option<String>,
}

impl DiscoveryManager {
    /// Create a new DiscoveryManager
    pub fn new() -> Result<Self, String> {
        let daemon =
            ServiceDaemon::new().map_err(|e| format!("Failed to create mDNS daemon: {}", e))?;

        Ok(DiscoveryManager {
            daemon,
            peers: Arc::new(RwLock::new(HashMap::new())),
            our_service: None,
        })
    }

    /// Register our service for others to discover
    pub fn register_service(
        &mut self,
        device_id: &str,
        device_name: &str,
        port: u16,
    ) -> Result<(), String> {
        let properties = [("id", device_id), ("name", device_name)];

        let service_info = ServiceInfo::new(
            SERVICE_TYPE,
            device_id,
            &format!("{}.local.", device_id),
            "",
            port,
            &properties[..],
        )
        .map_err(|e| format!("Failed to create service info: {}", e))?
        .enable_addr_auto();

        self.daemon
            .register(service_info)
            .map_err(|e| format!("Failed to register service: {}", e))?;

        self.our_service = Some(device_id.to_string());
        tracing::info!("Registered mDNS service: {} on port {}", device_id, port);

        Ok(())
    }

    /// Start browsing for peers
    pub fn browse(&self) -> Result<MdnsReceiver<ServiceEvent>, String> {
        self.daemon
            .browse(SERVICE_TYPE)
            .map_err(|e| format!("Failed to browse: {}", e))
    }

    /// Process discovery events and return discovered/lost peers
    pub async fn run_discovery(
        &self,
        event_tx: mpsc::Sender<DiscoveryEvent>,
    ) -> Result<(), String> {
        let receiver = self.browse()?;
        let peers = self.peers.clone();

        tokio::task::spawn_blocking(move || {
            while let Ok(event) = receiver.recv() {
                match event {
                    ServiceEvent::ServiceResolved(info) => {
                        let device_id = info
                            .get_property_val_str("id")
                            .unwrap_or_default()
                            .to_string();
                        let device_name = info
                            .get_property_val_str("name")
                            .unwrap_or_else(|| info.get_fullname())
                            .to_string();

                        let peer = DiscoveredPeer {
                            device_id: device_id.clone(),
                            device_name,
                            addresses: info
                                .get_addresses_v4()
                                .into_iter()
                                .map(IpAddr::V4)
                                .collect(),
                            port: info.get_port(),
                        };

                        {
                            let mut peers_guard = peers.write().unwrap();
                            peers_guard.insert(device_id.clone(), peer.clone());
                        }

                        let _ = event_tx.blocking_send(DiscoveryEvent::PeerFound(peer));
                    }
                    ServiceEvent::ServiceRemoved(_, fullname) => {
                        let device_id = fullname.split('.').next().unwrap_or("").to_string();
                        {
                            let mut peers_guard = peers.write().unwrap();
                            peers_guard.remove(&device_id);
                        }
                        let _ = event_tx.blocking_send(DiscoveryEvent::PeerLost(device_id));
                    }
                    _ => {}
                }
            }
        });

        Ok(())
    }

    /// Get currently known peers
    pub fn get_peers(&self) -> Vec<DiscoveredPeer> {
        let peers_guard = self.peers.read().unwrap();
        peers_guard.values().cloned().collect()
    }

    /// Unregister our service
    pub fn unregister(&mut self) {
        if let Some(ref service_id) = self.our_service {
            let fullname = format!("{}.{}", service_id, SERVICE_TYPE);
            let _ = self.daemon.unregister(&fullname);
            self.our_service = None;
        }
    }
}

impl Drop for DiscoveryManager {
    fn drop(&mut self) {
        self.unregister();
        let _ = self.daemon.shutdown();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_discovery_manager_creation() {
        let manager = DiscoveryManager::new();
        assert!(manager.is_ok());
    }
}
