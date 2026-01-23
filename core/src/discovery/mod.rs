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
    daemon: Option<ServiceDaemon>,
    peers: Arc<RwLock<HashMap<String, DiscoveredPeer>>>,
    our_service: Option<String>,
    fallback_mode: bool,
}

impl DiscoveryManager {
    /// Create a new DiscoveryManager with mDNS support
    pub fn new() -> Result<Self, String> {
        let daemon =
            ServiceDaemon::new().map_err(|e| format!("Failed to create mDNS daemon: {}", e))?;

        Ok(DiscoveryManager {
            daemon: Some(daemon),
            peers: Arc::new(RwLock::new(HashMap::new())),
            our_service: None,
            fallback_mode: false,
        })
    }

    /// Create a fallback DiscoveryManager without mDNS (for manual peers only)
    /// This always succeeds - manual peer addition should always work
    pub fn new_fallback() -> Self {
        tracing::info!("Creating fallback discovery manager (manual peers only)");
        DiscoveryManager {
            daemon: None,
            peers: Arc::new(RwLock::new(HashMap::new())),
            our_service: None,
            fallback_mode: true,
        }
    }

    /// Check if this is a fallback (no mDNS) manager
    pub fn is_fallback(&self) -> bool {
        self.fallback_mode
    }

    /// Register our service for others to discover
    pub fn register_service(
        &mut self,
        device_id: &str,
        device_name: &str,
        port: u16,
    ) -> Result<(), String> {
        // Include platform identifier for debugging cross-platform issues
        #[cfg(target_os = "windows")]
        let platform = "windows";
        #[cfg(target_os = "android")]
        let platform = "android";
        #[cfg(target_os = "macos")]
        let platform = "macos";
        #[cfg(target_os = "ios")]
        let platform = "ios";
        #[cfg(not(any(target_os = "windows", target_os = "android", target_os = "macos", target_os = "ios")))]
        let platform = "unknown";

        let properties = [
            ("id", device_id),
            ("name", device_name),
            ("platform", platform),
        ];

        // Generate a unique instance name using device_id
        let instance_name = device_id;

        // Use a simple, valid mDNS host label instead of a random opaque ID.
        // Address records are filled in by `enable_addr_auto`, so host name
        // uniqueness is not critical for connectivity.
        let hostname = "voidwarp.local.";

        tracing::info!(
            "Registering mDNS service: instance={}, hostname={}, port={}, platform={}",
            instance_name, hostname, port, platform
        );

        let service_info = ServiceInfo::new(
            SERVICE_TYPE,
            instance_name,
            &hostname,
            "", // Let mdns-sd auto-detect addresses
            port,
            &properties[..],
        )
        .map_err(|e| format!("Failed to create service info: {}", e))?
        .enable_addr_auto(); // Enable automatic address detection for all interfaces

        tracing::debug!("Service info created with auto-address detection enabled");

        let daemon = self.daemon.as_ref()
            .ok_or_else(|| "mDNS daemon not available".to_string())?;
        daemon
            .register(service_info)
            .map_err(|e| format!("Failed to register service: {}", e))?;

        self.our_service = Some(device_id.to_string());
        tracing::info!(
            "Successfully registered mDNS service: {} on port {} ({})",
            device_id, port, platform
        );

        Ok(())
    }

    /// Start browsing for peers
    pub fn browse(&self) -> Result<MdnsReceiver<ServiceEvent>, String> {
        tracing::info!("Starting mDNS browse for service type: {}", SERVICE_TYPE);
        let daemon = self.daemon.as_ref()
            .ok_or_else(|| "mDNS daemon not available (fallback mode)".to_string())?;
        daemon
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
        let our_id = self.our_service.clone();

        tokio::task::spawn_blocking(move || {
            tracing::info!("Discovery event loop started");
            while let Ok(event) = receiver.recv() {
                match event {
                    ServiceEvent::ServiceResolved(info) => {
                        let device_id = info
                            .get_property_val_str("id")
                            .unwrap_or_default()
                            .to_string();
                        
                        // Skip our own service
                        if let Some(ref our) = our_id {
                            if &device_id == our {
                                tracing::debug!("Skipping our own service: {}", device_id);
                                continue;
                            }
                        }

                        let device_name = info
                            .get_property_val_str("name")
                            .unwrap_or_else(|| info.get_fullname())
                            .to_string();
                        
                        let platform = info
                            .get_property_val_str("platform")
                            .unwrap_or("unknown")
                            .to_string();

                        // Collect IPs from mDNS response.
                        // Android environments often return v6-only entries depending on network,
                        // so we keep both v4 and v6.
                        // For resolved services mdns-sd returns `ScopedIp` (may include v6 + scope).
                        // Convert to plain `IpAddr` for FFI/UI.
                        let addresses: Vec<IpAddr> = info
                            .get_addresses()
                            .iter()
                            .filter_map(|ip| match ip {
                                mdns_sd::ScopedIp::V4(v4) => Some(IpAddr::V4(*v4.addr())),
                                mdns_sd::ScopedIp::V6(v6) => Some(IpAddr::V6(*v6.addr())),
                                _ => None,
                            })
                            .collect();

                        tracing::info!(
                            "Peer discovered: name='{}', id='{}', platform='{}', addresses={:?}, port={}",
                            device_name, device_id, platform, addresses, info.get_port()
                        );

                        let peer = DiscoveredPeer {
                            device_id: device_id.clone(),
                            device_name,
                            addresses,
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
                        tracing::info!("Peer removed: id='{}' (fullname='{}')", device_id, fullname);
                        {
                            let mut peers_guard = peers.write().unwrap();
                            peers_guard.remove(&device_id);
                        }
                        let _ = event_tx.blocking_send(DiscoveryEvent::PeerLost(device_id));
                    }
                    ServiceEvent::SearchStarted(stype) => {
                        tracing::debug!("mDNS search started for: {}", stype);
                    }
                    ServiceEvent::SearchStopped(stype) => {
                        tracing::debug!("mDNS search stopped for: {}", stype);
                    }
                    _ => {
                        tracing::trace!("Unhandled mDNS event: {:?}", event);
                    }
                }
            }
            tracing::info!("Discovery event loop ended");
        });

        Ok(())
    }

    /// Get currently known peers
    pub fn get_peers(&self) -> Vec<DiscoveredPeer> {
        let peers_guard = self.peers.read().unwrap();
        peers_guard.values().cloned().collect()
    }

    /// Start background browsing thread for FFI usage (no channel, just updates map)
    pub fn start_background_browsing(&self) -> Result<(), String> {
        let receiver = self.browse()?;
        let peers = self.peers.clone();
        let our_id = self.our_service.clone();

        std::thread::spawn(move || {
            tracing::info!("FFI Background discovery thread started");
            while let Ok(event) = receiver.recv() {
                match event {
                    ServiceEvent::ServiceResolved(info) => {
                        let device_id = info
                            .get_property_val_str("id")
                            .unwrap_or_default()
                            .to_string();
                        
                        // Skip our own service
                        if let Some(ref our) = our_id {
                            if &device_id == our {
                                continue;
                            }
                        }

                        let device_name = info
                            .get_property_val_str("name")
                            .unwrap_or_else(|| info.get_fullname())
                            .to_string();
                        
                        let platform = info
                            .get_property_val_str("platform")
                            .unwrap_or("unknown")
                            .to_string();

                        // Collect both v4 and v6 (see comment in async loop above).
                        let addresses: Vec<IpAddr> = info
                            .get_addresses()
                            .iter()
                            .filter_map(|ip| match ip {
                                mdns_sd::ScopedIp::V4(v4) => Some(IpAddr::V4(*v4.addr())),
                                mdns_sd::ScopedIp::V6(v6) => Some(IpAddr::V6(*v6.addr())),
                                _ => None,
                            })
                            .collect();

                        tracing::info!(
                            "FFI Peer discovered: name='{}', id='{}', platform='{}', addresses={:?}, port={}",
                            device_name, device_id, platform, addresses, info.get_port()
                        );

                        let peer = DiscoveredPeer {
                            device_id: device_id.clone(),
                            device_name,
                            addresses,
                            port: info.get_port(),
                        };

                        {
                            let mut peers_guard = peers.write().unwrap();
                            peers_guard.insert(device_id, peer);
                        }
                    }
                    ServiceEvent::ServiceRemoved(_, fullname) => {
                        let device_id = fullname.split('.').next().unwrap_or("").to_string();
                        tracing::info!("FFI Peer removed: id='{}'", device_id);
                        {
                            let mut peers_guard = peers.write().unwrap();
                            peers_guard.remove(&device_id);
                        }
                    }
                    _ => {}
                }
            }
            tracing::info!("FFI Background discovery thread ended");
        });

        Ok(())
    }

    /// Manually add a peer (e.g. for direct USB connection)
    pub fn add_manual_peer(
        &self,
        device_id: String,
        device_name: String,
        ip: IpAddr,
        port: u16,
    ) {
        let peer = DiscoveredPeer {
            device_id: device_id.clone(),
            device_name,
            addresses: vec![ip],
            port,
        };
        tracing::info!("Manually adding peer: {:?}", peer);
        let mut peers_guard = self.peers.write().unwrap();
        peers_guard.insert(device_id, peer);
    }

    /// Unregister our service
    pub fn unregister(&mut self) {
        if let Some(ref service_id) = self.our_service {
            if let Some(ref daemon) = self.daemon {
                let fullname = format!("{}.{}", service_id, SERVICE_TYPE);
                let _ = daemon.unregister(&fullname);
            }
            self.our_service = None;
        }
    }
}

impl Drop for DiscoveryManager {
    fn drop(&mut self) {
        self.unregister();
        if let Some(ref daemon) = self.daemon {
            let _ = daemon.shutdown();
        }
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
