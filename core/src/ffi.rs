//! FFI Module - C-ABI exports for platform integration
//!
//! This module provides the foreign function interface for calling
//! VoidWarp core functionality from C#, Swift, Kotlin, etc.
#![allow(clippy::not_unsafe_ptr_arg_deref)]

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::ptr;

use crate::discovery::DiscoveryManager;
use crate::security::crypto::{DeviceIdentity, PairingCode};

/// Opaque handle to the VoidWarp engine
pub struct VoidWarpHandle {
    discovery: Option<DiscoveryManager>,
    identity: DeviceIdentity,
}

/// Initialize the VoidWarp engine
/// Returns a handle that must be freed with `voidwarp_destroy`
#[no_mangle]
pub extern "C" fn voidwarp_init(device_name: *const c_char) -> *mut VoidWarpHandle {
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
/// Returns 0 on success, -1 on error
#[no_mangle]
pub extern "C" fn voidwarp_start_discovery(handle: *mut VoidWarpHandle, port: u16) -> i32 {
    if handle.is_null() {
        return -1;
    }

    let handle = unsafe { &mut *handle };

    match DiscoveryManager::new() {
        Ok(mut manager) => {
            if let Err(e) = manager.register_service(
                &handle.identity.device_id,
                &handle.identity.device_name,
                port,
            ) {
                tracing::error!("Failed to register service: {}", e);
                return -1;
            }
            handle.discovery = Some(manager);
            0
        }
        Err(e) => {
            tracing::error!("Failed to create discovery manager: {}", e);
            -1
        }
    }
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
            let ip_str = p
                .addresses
                .first()
                .map(|a| a.to_string())
                .unwrap_or_default();

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
