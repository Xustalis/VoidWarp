//! FFI Module - C-ABI exports for platform integration
//!
//! This module provides the foreign function interface for calling
//! VoidWarp core functionality from C#, Swift, Kotlin, etc.
#![allow(clippy::not_unsafe_ptr_arg_deref)]

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::sync::{Mutex, OnceLock};
use std::{net::IpAddr, net::Ipv4Addr, net::SocketAddr};

use crate::discovery::DiscoveryManager;
use crate::security::crypto::{DeviceIdentity, PairingCode};
use crate::transport::TransportServer;

/// Opaque handle to the VoidWarp engine
pub struct VoidWarpHandle {
    pub(crate) discovery: Option<DiscoveryManager>,
    pub(crate) identity: DeviceIdentity,
}

/// Initialize the VoidWarp engine
/// Returns a handle that must be freed with `voidwarp_destroy`
#[no_mangle]
pub extern "C" fn voidwarp_init(device_name: *const c_char) -> *mut VoidWarpHandle {
    // Make core logging best-effort. Never crash host process from init.
    crate::init();

    let name = if device_name.is_null() {
        "VoidWarp Device".to_string()
    } else {
        unsafe { CStr::from_ptr(device_name) }
            .to_string_lossy()
            .into_owned()
    };

    let identity = DeviceIdentity::generate(&name);

    let handle = Box::new(VoidWarpHandle {
        discovery: None,
        identity,
    });

    Box::into_raw(handle)
}

/// Destroy the VoidWarp engine handle
#[no_mangle]
pub extern "C" fn voidwarp_destroy(handle: *mut VoidWarpHandle) {
    if !handle.is_null() {
        unsafe {
            let _ = Box::from_raw(handle);
        }
    }
}

/// Get the device ID (caller must free with voidwarp_free_string)
#[no_mangle]
pub extern "C" fn voidwarp_get_device_id(handle: *const VoidWarpHandle) -> *mut c_char {
    if handle.is_null() {
        return ptr::null_mut();
    }

    let handle = unsafe { &*handle };
    match CString::new(handle.identity.device_id.clone()) {
        Ok(s) => s.into_raw(),
        Err(_) => ptr::null_mut(),
    }
}

/// Generate a new pairing code (caller must free with voidwarp_free_string)
#[no_mangle]
pub extern "C" fn voidwarp_generate_pairing_code() -> *mut c_char {
    let code = PairingCode::generate();
    match CString::new(code.display()) {
        Ok(s) => s.into_raw(),
        Err(_) => ptr::null_mut(),
    }
}

/// Free a string allocated by VoidWarp
#[no_mangle]
pub extern "C" fn voidwarp_free_string(s: *mut c_char) {
    if !s.is_null() {
        unsafe {
            let _ = CString::from_raw(s);
        }
    }
}

/// Start mDNS discovery
/// Returns 0 on success (even if mDNS partially fails - allows manual peers), never returns -1 anymore
/// This ensures manual peer addition always works even on restrictive networks
/// Start mDNS discovery (internal helper)
fn start_discovery_internal(
    handle: *mut VoidWarpHandle,
    port: u16,
    explicit_ip: Option<String>,
) -> i32 {
    // CRITICAL: Never unwind across FFI; on Android it aborts the process.
    let result = catch_unwind(AssertUnwindSafe(|| {
        if handle.is_null() {
            tracing::error!("start_discovery called with null handle");
            // Still return 0 to avoid crash loops - caller should check handle validity
            return 0;
        }

        let handle = unsafe { &mut *handle };

        // Try to create a proper mDNS discovery manager
        let manager = match DiscoveryManager::new() {
            Ok(mut mgr) => {
                tracing::info!("mDNS daemon created successfully");

                // Try to register service, but don't fail if it doesn't work
                if let Err(e) = mgr.register_service(
                    &handle.identity.device_id,
                    &handle.identity.device_name,
                    port,
                    explicit_ip,
                ) {
                    tracing::warn!("Failed to register mDNS service (continuing anyway): {}", e);
                }

                // Try to start background browsing (mDNS + UDP beacon listener), but don't fail if it doesn't work
                if let Err(e) = mgr.start_background_browsing(port) {
                    tracing::warn!("Failed to start mDNS browsing (continuing anyway): {}", e);
                }

                mgr
            }
            Err(e) => {
                // mDNS daemon failed to create - create a fallback manager for manual peers
                tracing::warn!("mDNS daemon unavailable ({}), using fallback mode", e);
                DiscoveryManager::new_fallback()
            }
        };

        handle.discovery = Some(manager);
        tracing::info!(
            "Discovery started (port: {}, mode: {})",
            port,
            if handle
                .discovery
                .as_ref()
                .map(|d| d.is_fallback())
                .unwrap_or(true)
            {
                "fallback"
            } else {
                "mDNS"
            }
        );

        // ALWAYS return 0 - we can always add manual peers even if mDNS doesn't work
        0
    }));

    match result {
        Ok(code) => code,
        Err(_) => {
            tracing::error!("panic caught in start_discovery, returning success anyway");
            // Even on panic, return 0 to allow manual peer addition
            0
        }
    }
}

/// Start mDNS discovery (auto-detect IP)
/// Returns 0 on success
#[no_mangle]
pub extern "C" fn voidwarp_start_discovery(handle: *mut VoidWarpHandle, port: u16) -> i32 {
    start_discovery_internal(handle, port, None)
}

/// Start mDNS discovery with explicit IP (for reliable Android discovery)
/// Returns 0 on success
#[no_mangle]
pub extern "C" fn voidwarp_start_discovery_with_ip(
    handle: *mut VoidWarpHandle,
    port: u16,
    ip_address: *const c_char,
) -> i32 {
    let explicit_ip = if !ip_address.is_null() {
        unsafe { CStr::from_ptr(ip_address) }
            .to_str()
            .ok()
            .map(|s| s.to_string())
    } else {
        None
    };

    start_discovery_internal(handle, port, explicit_ip)
}

/// Stop mDNS discovery
#[no_mangle]
pub extern "C" fn voidwarp_stop_discovery(handle: *mut VoidWarpHandle) {
    if handle.is_null() {
        return;
    }

    let handle = unsafe { &mut *handle };
    handle.discovery = None;
}

/// Manually add a peer (e.g. for localhost connection)
#[no_mangle]
pub extern "C" fn voidwarp_add_manual_peer(
    handle: *mut VoidWarpHandle,
    device_id: *const c_char,
    device_name: *const c_char,
    ip_address: *const c_char,
    port: u16,
) -> i32 {
    if handle.is_null() || device_id.is_null() || device_name.is_null() || ip_address.is_null() {
        return -1;
    }

    let handle = unsafe { &mut *handle };
    let discovery = match &handle.discovery {
        Some(d) => d,
        None => return -1,
    };

    let device_id = unsafe { CStr::from_ptr(device_id) }
        .to_string_lossy()
        .to_string();
    let device_name = unsafe { CStr::from_ptr(device_name) }
        .to_string_lossy()
        .to_string();
    let ip_str = unsafe { CStr::from_ptr(ip_address) }.to_string_lossy();

    let ip: std::net::IpAddr = match ip_str.parse() {
        Ok(ip) => ip,
        Err(_) => return -1,
    };

    discovery.add_manual_peer(device_id, device_name, ip, port);
    0
}

/// Discovered peer info for FFI
#[repr(C)]
pub struct FfiPeer {
    pub device_id: *mut c_char,
    pub device_name: *mut c_char,
    pub ip_address: *mut c_char,
    pub port: u16,
}

/// Peer list for FFI
#[repr(C)]
pub struct FfiPeerList {
    pub peers: *mut FfiPeer,
    pub count: usize,
}

/// Get list of discovered peers
/// Caller must free with voidwarp_free_peer_list
#[no_mangle]
pub extern "C" fn voidwarp_get_peers(handle: *const VoidWarpHandle) -> FfiPeerList {
    let empty = FfiPeerList {
        peers: ptr::null_mut(),
        count: 0,
    };

    if handle.is_null() {
        return empty;
    }

    let handle = unsafe { &*handle };
    let discovery = match &handle.discovery {
        Some(d) => d,
        None => return empty,
    };

    let peers = discovery.get_peers();
    if peers.is_empty() {
        return empty;
    }

    let mut ffi_peers: Vec<FfiPeer> = peers
        .iter()
        .map(|p| {
            // Return ALL valid IPs as a comma-separated string
            // This allows the UI to show them all or try them sequentially
            let valid_ips: Vec<String> = p
                .addresses
                .iter()
                .filter(|ip| match ip {
                    std::net::IpAddr::V4(ipv4) => !ipv4.is_loopback() && !ipv4.is_link_local(),
                    _ => false, // Focusing on IPv4 for now due to Android/Windows cross-compatibility quirks
                })
                .map(|ip| ip.to_string())
                .collect();

            // Sort them to prioritize 192.168.x.x (typical home/office wifi)
            let mut sorted_ips = valid_ips;
            sorted_ips.sort_by(|a, b| {
                let a_is_local = a.starts_with("192.168.");
                let b_is_local = b.starts_with("192.168.");
                if a_is_local && !b_is_local {
                    std::cmp::Ordering::Less
                } else if !a_is_local && b_is_local {
                    std::cmp::Ordering::Greater
                } else {
                    a.cmp(b)
                }
            });

            let ip_str = sorted_ips.join(",");

            FfiPeer {
                device_id: CString::new(p.device_id.clone())
                    .map(|s| s.into_raw())
                    .unwrap_or(ptr::null_mut()),
                device_name: CString::new(p.device_name.clone())
                    .map(|s| s.into_raw())
                    .unwrap_or(ptr::null_mut()),
                ip_address: CString::new(ip_str)
                    .map(|s| s.into_raw())
                    .unwrap_or(ptr::null_mut()),
                port: p.port,
            }
        })
        .collect();

    // Ensure capacity equals length for safe reconstruction later
    ffi_peers.shrink_to_fit();
    if ffi_peers.capacity() != ffi_peers.len() {
        let mut exact = Vec::with_capacity(ffi_peers.len());
        exact.extend(ffi_peers);
        ffi_peers = exact;
    }

    let count = ffi_peers.len();
    let ptr = ffi_peers.as_mut_ptr();
    std::mem::forget(ffi_peers);

    FfiPeerList { peers: ptr, count }
}

/// Free a peer list
#[no_mangle]
pub extern "C" fn voidwarp_free_peer_list(list: FfiPeerList) {
    if list.peers.is_null() || list.count == 0 {
        return;
    }

    unsafe {
        let peers = Vec::from_raw_parts(list.peers, list.count, list.count);
        for peer in peers {
            voidwarp_free_string(peer.device_id);
            voidwarp_free_string(peer.device_name);
            voidwarp_free_string(peer.ip_address);
        }
    }
}

// ============================================================================
// File Transfer FFI
// ============================================================================

use crate::transfer::{FileSender, TransferProgress, TransferState};
use std::path::Path;

/// Transfer progress for FFI
#[repr(C)]
pub struct FfiTransferProgress {
    pub bytes_transferred: u64,
    pub total_bytes: u64,
    pub percentage: f32,
    pub speed_mbps: f32,
    pub state: i32, // 0=Pending, 1=Transferring, 2=Paused, 3=Completed, 4=Failed, 5=Cancelled
}

impl From<TransferProgress> for FfiTransferProgress {
    fn from(p: TransferProgress) -> Self {
        FfiTransferProgress {
            bytes_transferred: p.bytes_transferred,
            total_bytes: p.total_bytes,
            percentage: p.percentage(),
            speed_mbps: (p.speed_bytes_per_sec as f32) / (1024.0 * 1024.0),
            state: match p.state {
                TransferState::Pending => 0,
                TransferState::Transferring => 1,
                TransferState::Paused => 2,
                TransferState::Completed => 3,
                TransferState::Failed => 4,
                TransferState::Cancelled => 5,
            },
        }
    }
}

/// Progress callback type
pub type ProgressCallback = extern "C" fn(FfiTransferProgress, *mut std::ffi::c_void);

/// Opaque handle to a file sender
pub struct FfiFileSender {
    sender: FileSender,
}

/// Create a file sender for the given path
/// Returns null on error
#[no_mangle]
pub extern "C" fn voidwarp_create_sender(path: *const c_char) -> *mut FfiFileSender {
    if path.is_null() {
        return ptr::null_mut();
    }

    let path_str = unsafe { CStr::from_ptr(path) }.to_string_lossy();
    let path = Path::new(path_str.as_ref());

    match FileSender::new(path) {
        Ok(sender) => Box::into_raw(Box::new(FfiFileSender { sender })),
        Err(e) => {
            tracing::error!("Failed to create sender: {}", e);
            ptr::null_mut()
        }
    }
}

/// Get file metadata from sender
#[no_mangle]
pub extern "C" fn voidwarp_sender_get_size(sender: *const FfiFileSender) -> u64 {
    if sender.is_null() {
        return 0;
    }
    unsafe { (*sender).sender.metadata().size }
}

/// Get file name (caller must free)
#[no_mangle]
pub extern "C" fn voidwarp_sender_get_name(sender: *const FfiFileSender) -> *mut c_char {
    if sender.is_null() {
        return ptr::null_mut();
    }
    let name = unsafe { &(*sender).sender.metadata().name };
    CString::new(name.clone())
        .map(|s| s.into_raw())
        .unwrap_or(ptr::null_mut())
}

/// Read next chunk from sender
/// Returns chunk data that must be freed with voidwarp_free_chunk
#[repr(C)]
pub struct FfiChunk {
    pub index: u64,
    pub data: *mut u8,
    pub len: usize,
    pub is_last: bool,
}

#[no_mangle]
pub extern "C" fn voidwarp_sender_read_chunk(sender: *mut FfiFileSender) -> FfiChunk {
    let empty = FfiChunk {
        index: 0,
        data: ptr::null_mut(),
        len: 0,
        is_last: true,
    };

    if sender.is_null() {
        return empty;
    }

    let sender = unsafe { &mut (*sender).sender };
    match sender.read_chunk() {
        Ok(Some((index, data))) => {
            let len = data.len();
            let is_last = index + 1 >= sender.metadata().total_chunks;
            let mut boxed = data.into_boxed_slice();
            let ptr = boxed.as_mut_ptr();
            std::mem::forget(boxed);

            FfiChunk {
                index,
                data: ptr,
                len,
                is_last,
            }
        }
        _ => empty,
    }
}

#[no_mangle]
pub extern "C" fn voidwarp_free_chunk(chunk: FfiChunk) {
    if !chunk.data.is_null() && chunk.len > 0 {
        unsafe {
            let _ = Vec::from_raw_parts(chunk.data, chunk.len, chunk.len);
        }
    }
}

/// Get current progress from sender
#[no_mangle]
pub extern "C" fn voidwarp_sender_get_progress(
    sender: *const FfiFileSender,
) -> FfiTransferProgress {
    if sender.is_null() {
        return FfiTransferProgress {
            bytes_transferred: 0,
            total_bytes: 0,
            percentage: 0.0,
            speed_mbps: 0.0,
            state: 4,
        };
    }

    unsafe { (*sender).sender.get_progress().into() }
}

/// Cancel transfer
#[no_mangle]
pub extern "C" fn voidwarp_sender_cancel(sender: *mut FfiFileSender) {
    if !sender.is_null() {
        unsafe {
            (*sender).sender.cancel();
        }
    }
}

/// Destroy sender
#[no_mangle]
pub extern "C" fn voidwarp_destroy_sender(sender: *mut FfiFileSender) {
    if !sender.is_null() {
        unsafe {
            let _ = Box::from_raw(sender);
        }
    }
}

// ============================================================================
// File Receiver FFI
// ============================================================================

use crate::receiver::{FileReceiverServer, ReceiverState};
use std::path::PathBuf;

/// Opaque handle to a file receiver server
pub struct FfiFileReceiver {
    server: FileReceiverServer,
}

/// Pending transfer info for FFI
#[repr(C)]
pub struct FfiPendingTransfer {
    pub sender_name: *mut c_char,
    pub sender_addr: *mut c_char,
    pub file_name: *mut c_char,
    pub file_size: u64,
    pub is_valid: bool,
}

/// Create a file receiver server
/// Returns null on error
#[no_mangle]
pub extern "C" fn voidwarp_create_receiver() -> *mut FfiFileReceiver {
    match FileReceiverServer::new() {
        Ok(server) => Box::into_raw(Box::new(FfiFileReceiver { server })),
        Err(e) => {
            tracing::error!("Failed to create receiver: {}", e);
            ptr::null_mut()
        }
    }
}

/// Get the port the receiver is listening on
#[no_mangle]
pub extern "C" fn voidwarp_receiver_get_port(receiver: *const FfiFileReceiver) -> u16 {
    if receiver.is_null() {
        return 0;
    }
    unsafe { (*receiver).server.port() }
}

/// Start listening for incoming transfers
#[no_mangle]
pub extern "C" fn voidwarp_receiver_start(receiver: *mut FfiFileReceiver) {
    if receiver.is_null() {
        return;
    }
    unsafe { (*receiver).server.start() }
}

/// Stop listening
#[no_mangle]
pub extern "C" fn voidwarp_receiver_stop(receiver: *mut FfiFileReceiver) {
    if receiver.is_null() {
        return;
    }
    unsafe { (*receiver).server.stop() }
}

/// Get receiver state
/// Returns: 0=Idle, 1=Listening, 2=AwaitingAccept, 3=Receiving, 4=Completed, 5=Error
#[no_mangle]
pub extern "C" fn voidwarp_receiver_get_state(receiver: *const FfiFileReceiver) -> i32 {
    if receiver.is_null() {
        return 5; // Error
    }
    match unsafe { (*receiver).server.state() } {
        ReceiverState::Idle => 0,
        ReceiverState::Listening => 1,
        ReceiverState::AwaitingAccept => 2,
        ReceiverState::Receiving => 3,
        ReceiverState::Completed => 4,
        ReceiverState::Error => 5,
    }
}

/// Get pending transfer info
/// The returned struct's strings must be freed with voidwarp_free_string
/// Check is_valid field to see if there is a pending transfer
#[no_mangle]
pub extern "C" fn voidwarp_receiver_get_pending(
    receiver: *const FfiFileReceiver,
) -> FfiPendingTransfer {
    let empty = FfiPendingTransfer {
        sender_name: ptr::null_mut(),
        sender_addr: ptr::null_mut(),
        file_name: ptr::null_mut(),
        file_size: 0,
        is_valid: false,
    };

    if receiver.is_null() {
        return empty;
    }

    match unsafe { (*receiver).server.pending_transfer() } {
        Some(transfer) => FfiPendingTransfer {
            sender_name: CString::new(transfer.sender_name)
                .map(|s| s.into_raw())
                .unwrap_or(ptr::null_mut()),
            sender_addr: CString::new(transfer.sender_addr.to_string())
                .map(|s| s.into_raw())
                .unwrap_or(ptr::null_mut()),
            file_name: CString::new(transfer.file_name)
                .map(|s| s.into_raw())
                .unwrap_or(ptr::null_mut()),
            file_size: transfer.file_size,
            is_valid: true,
        },
        None => empty,
    }
}

/// Free a pending transfer struct's strings
#[no_mangle]
pub extern "C" fn voidwarp_free_pending_transfer(transfer: FfiPendingTransfer) {
    voidwarp_free_string(transfer.sender_name);
    voidwarp_free_string(transfer.sender_addr);
    voidwarp_free_string(transfer.file_name);
}

/// Accept the pending transfer and save to the given path
/// Returns 0 on success, -1 on error
#[no_mangle]
pub extern "C" fn voidwarp_receiver_accept(
    receiver: *mut FfiFileReceiver,
    save_path: *const c_char,
) -> i32 {
    if receiver.is_null() || save_path.is_null() {
        return -1;
    }

    let path_str = unsafe { CStr::from_ptr(save_path) }.to_string_lossy();
    let path = PathBuf::from(path_str.as_ref());

    match unsafe { (*receiver).server.accept_transfer(&path) } {
        Ok(_) => 0,
        Err(e) => {
            tracing::error!("Accept transfer failed: {}", e);
            -1
        }
    }
}

/// Reject the pending transfer
/// Returns 0 on success, -1 on error
#[no_mangle]
pub extern "C" fn voidwarp_receiver_reject(receiver: *mut FfiFileReceiver) -> i32 {
    if receiver.is_null() {
        return -1;
    }

    match unsafe { (*receiver).server.reject_transfer() } {
        Ok(_) => 0,
        Err(e) => {
            tracing::error!("Reject transfer failed: {}", e);
            -1
        }
    }
}

/// Get receive progress (0-100)
#[no_mangle]
pub extern "C" fn voidwarp_receiver_get_progress(receiver: *const FfiFileReceiver) -> f32 {
    if receiver.is_null() {
        return 0.0;
    }
    unsafe { (*receiver).server.progress() }
}

/// Get bytes received so far
#[no_mangle]
pub extern "C" fn voidwarp_receiver_get_bytes_received(receiver: *const FfiFileReceiver) -> u64 {
    if receiver.is_null() {
        return 0;
    }
    unsafe { (*receiver).server.bytes_received() }
}

/// Destroy receiver
#[no_mangle]
pub extern "C" fn voidwarp_destroy_receiver(receiver: *mut FfiFileReceiver) {
    if !receiver.is_null() {
        unsafe {
            let _ = Box::from_raw(receiver);
        }
    }
}

// ============================================================================
// TCP File Sender FFI (for Windows P/Invoke)
// ============================================================================

use crate::sender::{TcpFileSender, TransferResult};

/// Opaque handle to a TCP file sender
pub struct FfiTcpSender {
    sender: TcpFileSender,
}

/// Create a TCP file sender for the given path
/// Returns null on error
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_create(path: *const c_char) -> *mut FfiTcpSender {
    if path.is_null() {
        return ptr::null_mut();
    }

    let path_str = unsafe { CStr::from_ptr(path) }.to_string_lossy();

    match TcpFileSender::new(&path_str) {
        Ok(sender) => Box::into_raw(Box::new(FfiTcpSender { sender })),
        Err(e) => {
            tracing::error!("Failed to create TCP sender: {}", e);
            ptr::null_mut()
        }
    }
}

/// Set chunk size for the sender (in bytes)
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_set_chunk_size(sender: *mut FfiTcpSender, size: usize) {
    if !sender.is_null() && size > 0 {
        unsafe {
            (*sender).sender.set_chunk_size(size);
        }
    }
}

/// Start TCP transfer to the target address
/// Returns: 0=Success, 1=Rejected, 2=ChecksumMismatch, 3=ConnectionFailed, 4=Timeout, 5=Cancelled, 6=IoError
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_start(
    sender: *const FfiTcpSender,
    ip_address: *const c_char,
    port: u16,
    sender_name: *const c_char,
) -> i32 {
    if sender.is_null() || ip_address.is_null() || sender_name.is_null() {
        return 3; // ConnectionFailed
    }

    let sender_ref = unsafe { &(*sender).sender };
    let ip_str = unsafe { CStr::from_ptr(ip_address) }.to_string_lossy();
    let name_str = unsafe { CStr::from_ptr(sender_name) }.to_string_lossy();

    let ip: std::net::IpAddr = match ip_str.parse() {
        Ok(ip) => ip,
        Err(_) => return 3, // ConnectionFailed - invalid IP
    };

    let peer_addr = std::net::SocketAddr::new(ip, port);

    match sender_ref.send_to(peer_addr, &name_str) {
        TransferResult::Success => 0,
        TransferResult::Rejected => 1,
        TransferResult::ChecksumMismatch => 2,
        TransferResult::ConnectionFailed(_) => 3,
        TransferResult::Timeout => 4,
        TransferResult::Cancelled => 5,
        TransferResult::IoError(_) => 6,
    }
}

/// Get file checksum (caller must free with voidwarp_free_string)
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_get_checksum(sender: *const FfiTcpSender) -> *mut c_char {
    if sender.is_null() {
        return ptr::null_mut();
    }
    let checksum = unsafe { (*sender).sender.checksum() };
    CString::new(checksum)
        .map(|s| s.into_raw())
        .unwrap_or(ptr::null_mut())
}

/// Get file size
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_get_file_size(sender: *const FfiTcpSender) -> u64 {
    if sender.is_null() {
        return 0;
    }
    unsafe { (*sender).sender.file_size() }
}

/// Get transfer progress (0-100)
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_get_progress(sender: *const FfiTcpSender) -> f32 {
    if sender.is_null() {
        return 0.0;
    }
    unsafe { (*sender).sender.progress() }
}

/// Cancel the transfer
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_cancel(sender: *const FfiTcpSender) {
    if !sender.is_null() {
        unsafe {
            (*sender).sender.cancel();
        }
    }
}

/// Test connection to a peer
/// Returns: 0=Success, 3=ConnectionFailed
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_test_link(ip_address: *const c_char, port: u16) -> i32 {
    if ip_address.is_null() {
        return 3;
    }

    let ip_str = unsafe { CStr::from_ptr(ip_address) }.to_string_lossy();
    let ip: std::net::IpAddr = match ip_str.parse() {
        Ok(ip) => ip,
        Err(_) => return 3,
    };

    let peer_addr = std::net::SocketAddr::new(ip, port);

    match TcpFileSender::test_connection(peer_addr) {
        TransferResult::Success => 0,
        _ => 3,
    }
}

/// Destroy the sender
#[no_mangle]
pub extern "C" fn voidwarp_tcp_sender_destroy(sender: *mut FfiTcpSender) {
    if !sender.is_null() {
        unsafe {
            let _ = Box::from_raw(sender);
        }
    }
}

static TRANSPORT_SERVER: OnceLock<Mutex<Option<TransportServer>>> = OnceLock::new();

fn transport_server_cell() -> &'static Mutex<Option<TransportServer>> {
    TRANSPORT_SERVER.get_or_init(|| Mutex::new(None))
}

#[no_mangle]
pub extern "C" fn voidwarp_transport_start_server(port: u16) -> bool {
    let mut cell = transport_server_cell().lock().unwrap();
    if cell.is_some() {
        return true;
    }
    let addr = SocketAddr::new(IpAddr::V4(Ipv4Addr::UNSPECIFIED), port);
    match TransportServer::bind(addr) {
        Ok(server) => {
            *cell = Some(server);
            true
        }
        Err(_) => false,
    }
}

#[no_mangle]
pub extern "C" fn voidwarp_transport_ping(ip_address: *const c_char, port: u16) -> bool {
    if ip_address.is_null() {
        return false;
    }

    let ip_str = unsafe { CStr::from_ptr(ip_address) }.to_string_lossy();
    let ip: std::net::IpAddr = match ip_str.parse() {
        Ok(ip) => ip,
        Err(_) => return false,
    };

    let addr = std::net::SocketAddr::new(ip, port);

    // Just try to connect - if we can connect, the port is open and reachable.
    // We don't need a full protocol handshake for a basic liveness check.
    std::net::TcpStream::connect_timeout(&addr, std::time::Duration::from_secs(2)).is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_init_destroy() {
        let name = CString::new("Test Device").unwrap();
        let handle = voidwarp_init(name.as_ptr());
        assert!(!handle.is_null());

        let device_id = voidwarp_get_device_id(handle);
        assert!(!device_id.is_null());
        voidwarp_free_string(device_id);

        voidwarp_destroy(handle);
    }

    #[test]
    fn test_pairing_code() {
        let code = voidwarp_generate_pairing_code();
        assert!(!code.is_null());

        let code_str = unsafe { CStr::from_ptr(code) }.to_string_lossy();
        assert!(code_str.contains('-'));

        voidwarp_free_string(code);
    }
}
