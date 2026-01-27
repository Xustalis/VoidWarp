#![cfg(target_os = "android")]
#![allow(non_snake_case)]

use crate::discovery::{DiscoveredPeer, DiscoveryManager};
use crate::ffi;
use jni::objects::{JClass, JObject, JString, JValue};
use jni::sys::{jboolean, jint, jlong, jobject, jobjectArray, jstring};
use jni::JNIEnv;
use std::ffi::{CStr, CString};

/// Convert JString to CString
fn get_string(env: &mut JNIEnv, string: JString) -> CString {
    let input: String = env
        .get_string(&string)
        .expect("Couldn't get java string!")
        .into();
    CString::new(input).unwrap_or_default()
}

/// Convert JString to String
fn get_rust_string(env: &mut JNIEnv, string: JString) -> String {
    env.get_string(&string)
        .expect("Couldn't get java string!")
        .into()
}

/// Convert *mut c_char to JString
unsafe fn from_c_string(env: &mut JNIEnv, ptr: *mut std::os::raw::c_char) -> jstring {
    if ptr.is_null() {
        return JObject::null().into_raw();
    }
    let c_str = CStr::from_ptr(ptr);
    let s = c_str.to_string_lossy();
    // We must free the string if it was allocated by voidwarp functions
    // But here we just convert. The caller handles freeing via voidwarp_free_string if needed.
    // Wait, voidwarp_get_device_id returns a new malloc'd string that MUST be freed.
    // So we should free it after converting.
    let output = env.new_string(&*s).expect("Couldn't create java string!");
    output.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpInit(
    mut env: JNIEnv,
    _class: JClass,
    device_name: JString,
) -> jlong {
    let name = get_string(&mut env, device_name);
    let handle = ffi::voidwarp_init(name.as_ptr());
    handle as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpDestroy(
    _env: JNIEnv,
    _class: JClass,
    handle: jlong,
) {
    ffi::voidwarp_destroy(handle as *mut ffi::VoidWarpHandle);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGetDeviceId(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
) -> jstring {
    let ptr = ffi::voidwarp_get_device_id(handle as *const ffi::VoidWarpHandle);
    let jstr = from_c_string(&mut env, ptr);
    ffi::voidwarp_free_string(ptr); // Free the Rust-allocated string
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGeneratePairingCode(
    mut env: JNIEnv,
    _class: JClass,
) -> jstring {
    let ptr = ffi::voidwarp_generate_pairing_code();
    let jstr = from_c_string(&mut env, ptr);
    ffi::voidwarp_free_string(ptr);
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpStartDiscovery(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
    ip_address: JString,
    port: jint,
) -> jint {
    let ip = get_string(&mut env, ip_address);
    ffi::voidwarp_start_discovery_with_ip(
        handle as *mut ffi::VoidWarpHandle,
        port as u16,
        ip.as_ptr(),
    )
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpStopDiscovery(
    _env: JNIEnv,
    _class: JClass,
    handle: jlong,
) {
    ffi::voidwarp_stop_discovery(handle as *mut ffi::VoidWarpHandle);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpAddManualPeer(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
    device_id: JString,
    device_name: JString,
    ip_address: JString,
    port: jint,
) -> jint {
    let handle_ptr = handle as *const ffi::VoidWarpHandle;
    if handle_ptr.is_null() {
        return -1;
    }
    let handle = &*handle_ptr;
    let discovery = match &handle.discovery {
        Some(d) => d,
        None => return -1,
    };

    let id = get_rust_string(&mut env, device_id);
    let name = get_rust_string(&mut env, device_name);
    let ip_str = get_rust_string(&mut env, ip_address);

    let ip: std::net::IpAddr = match ip_str.parse() {
        Ok(ip) => ip,
        Err(_) => return -2,
    };

    discovery.add_manual_peer(id, name, ip, port as u16);
    0
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGetPeers(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
) -> jobjectArray {
    let list = ffi::voidwarp_get_peers(handle as *const ffi::VoidWarpHandle);

    // Create PeerInfo array
    let peer_class = env
        .find_class("com/voidwarp/android/native/NativeLib$PeerInfo")
        .expect("Could not find PeerInfo class");

    let initial_element = JObject::null();
    let output_array = env
        .new_object_array(list.count as i32, &peer_class, &initial_element)
        .expect("Could not create array");

    if list.count > 0 && !list.peers.is_null() {
        let peers_slice = std::slice::from_raw_parts(list.peers, list.count);
        for (i, peer) in peers_slice.iter().enumerate() {
            let id = if peer.device_id.is_null() {
                std::borrow::Cow::from("")
            } else {
                CStr::from_ptr(peer.device_id).to_string_lossy()
            };

            let name = if peer.device_name.is_null() {
                std::borrow::Cow::from("Unknown")
            } else {
                CStr::from_ptr(peer.device_name).to_string_lossy()
            };

            let ip = if peer.ip_address.is_null() {
                std::borrow::Cow::from("")
            } else {
                CStr::from_ptr(peer.ip_address).to_string_lossy()
            };

            let j_id = env.new_string(&*id).unwrap();
            let j_name = env.new_string(&*name).unwrap();
            let j_ip = env.new_string(&*ip).unwrap();

            // Constructor: (String, String, String, Int)
            let obj = env
                .new_object(
                    &peer_class,
                    "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)V",
                    &[
                        JValue::Object(&j_id),
                        JValue::Object(&j_name),
                        JValue::Object(&j_ip),
                        JValue::Int(peer.port as i32),
                    ],
                )
                .expect("Failed to create PeerInfo object");

            env.set_object_array_element(&output_array, i as i32, &obj)
                .expect("Failed to set array element");
        }
    }

    ffi::voidwarp_free_peer_list(list);
    output_array.into_raw()
}

// File Transfer bindings
#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpCreateSender(
    mut env: JNIEnv,
    _class: JClass,
    path: JString,
) -> jlong {
    let path_c = get_string(&mut env, path);
    let sender = ffi::voidwarp_create_sender(path_c.as_ptr());
    sender as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetSize(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jlong {
    ffi::voidwarp_sender_get_size(sender as *const ffi::FfiFileSender) as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetName(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jstring {
    let ptr = ffi::voidwarp_sender_get_name(sender as *const ffi::FfiFileSender);
    let jstr = from_c_string(&mut env, ptr);
    // name is a new CString, so we must free it?
    // ffi source: CString::new(...).into_raw(). Caller must free.
    // Yes, we must free. BUT ffi doesn't expose voidwarp_free_string from generic pointer?
    // It's just voidwarp_free_string(s: *mut c_char).
    ffi::voidwarp_free_string(ptr);
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetProgress(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jobject {
    let progress = ffi::voidwarp_sender_get_progress(sender as *const ffi::FfiFileSender);

    let progress_class = env
        .find_class("com/voidwarp/android/native/NativeLib$TransferProgress")
        .expect("Could not find TransferProgress class");

    // Constructor: (Long, Long, Float, Float, Int)
    let obj = env
        .new_object(
            &progress_class,
            "(JJFFI)V",
            &[
                JValue::Long(progress.bytes_transferred as i64),
                JValue::Long(progress.total_bytes as i64),
                JValue::Float(progress.percentage),
                JValue::Float(progress.speed_mbps),
                JValue::Int(progress.state),
            ],
        )
        .expect("Failed to create TransferProgress object");

    obj.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderCancel(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    ffi::voidwarp_sender_cancel(sender as *mut ffi::FfiFileSender);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpDestroySender(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    ffi::voidwarp_destroy_sender(sender as *mut ffi::FfiFileSender);
}

// ============================================================================
// File Receiver JNI Bindings
// ============================================================================

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpCreateReceiver(
    _env: JNIEnv,
    _class: JClass,
) -> jlong {
    ffi::voidwarp_create_receiver() as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverGetPort(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> jint {
    ffi::voidwarp_receiver_get_port(receiver as *const ffi::FfiFileReceiver) as jint
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverStart(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) {
    ffi::voidwarp_receiver_start(receiver as *mut ffi::FfiFileReceiver);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverStop(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) {
    ffi::voidwarp_receiver_stop(receiver as *mut ffi::FfiFileReceiver);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverGetState(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> jint {
    ffi::voidwarp_receiver_get_state(receiver as *const ffi::FfiFileReceiver)
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverGetPending(
    mut env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> jobject {
    let pending = ffi::voidwarp_receiver_get_pending(receiver as *const ffi::FfiFileReceiver);

    if !pending.is_valid {
        return JObject::null().into_raw();
    }

    // Create PendingTransfer object
    let class = env
        .find_class("com/voidwarp/android/native/NativeLib$PendingTransfer")
        .expect("Failed to find PendingTransfer class");

    let sender_name = from_c_string(&mut env, pending.sender_name);
    let sender_addr = from_c_string(&mut env, pending.sender_addr);
    let file_name = from_c_string(&mut env, pending.file_name);

    // Save file_size before freeing
    let file_size = pending.file_size;

    // Free the C strings
    ffi::voidwarp_free_pending_transfer(pending);

    let obj = env
        .new_object(
            &class,
            "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;J)V",
            &[
                JValue::Object(&JObject::from_raw(sender_name)),
                JValue::Object(&JObject::from_raw(sender_addr)),
                JValue::Object(&JObject::from_raw(file_name)),
                JValue::Long(file_size as i64),
            ],
        )
        .expect("Failed to create PendingTransfer object");

    obj.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverAccept(
    mut env: JNIEnv,
    _class: JClass,
    receiver: jlong,
    save_path: JString,
) -> jint {
    let path = get_string(&mut env, save_path);
    ffi::voidwarp_receiver_accept(receiver as *mut ffi::FfiFileReceiver, path.as_ptr())
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverReject(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> jint {
    ffi::voidwarp_receiver_reject(receiver as *mut ffi::FfiFileReceiver)
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverGetProgress(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> f32 {
    ffi::voidwarp_receiver_get_progress(receiver as *const ffi::FfiFileReceiver)
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpReceiverGetBytesReceived(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) -> jlong {
    ffi::voidwarp_receiver_get_bytes_received(receiver as *const ffi::FfiFileReceiver) as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpDestroyReceiver(
    _env: JNIEnv,
    _class: JClass,
    receiver: jlong,
) {
    ffi::voidwarp_destroy_receiver(receiver as *mut ffi::FfiFileReceiver);
}

// ============================================================================
// Checksum JNI Bindings
// ============================================================================

use crate::checksum;

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpCalculateFileChecksum(
    mut env: JNIEnv,
    _class: JClass,
    file_path: JString,
) -> jstring {
    let path_str = get_string(&mut env, file_path);
    let path = std::path::Path::new(path_str.to_str().unwrap_or(""));

    match checksum::calculate_file_checksum(path) {
        Ok(hash) => {
            let jstr = env
                .new_string(&hash)
                .expect("Failed to create checksum string");
            jstr.into_raw()
        }
        Err(e) => {
            tracing::error!("Checksum calculation failed: {}", e);
            JObject::null().into_raw()
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpVerifyFileChecksum(
    mut env: JNIEnv,
    _class: JClass,
    file_path: JString,
    expected_checksum: JString,
) -> jint {
    let path_str = get_string(&mut env, file_path);
    let expected_str = get_string(&mut env, expected_checksum);
    let path = std::path::Path::new(path_str.to_str().unwrap_or(""));

    match checksum::verify_file_checksum(path, expected_str.to_str().unwrap_or("")) {
        Ok(true) => 1,  // Match
        Ok(false) => 0, // Mismatch
        Err(_) => -1,   // Error
    }
}

// ============================================================================
// TCP Sender JNI Bindings
// ============================================================================

use crate::sender::TcpFileSender;

// We use raw pointers (jlong) to store TcpFileSender instances on the Java side.
// No global map is needed.

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderCreate(
    mut env: JNIEnv,
    _class: JClass,
    file_path: JString,
) -> jlong {
    let path_str = get_string(&mut env, file_path);

    match TcpFileSender::new(path_str.to_str().unwrap_or("")) {
        Ok(sender) => {
            let boxed = Box::new(sender);
            Box::into_raw(boxed) as jlong
        }
        Err(e) => {
            tracing::error!("Failed to create TcpFileSender: {}", e);
            0
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderGetChecksum(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jstring {
    if sender == 0 {
        return JObject::null().into_raw();
    }
    let sender_ref = &*(sender as *const TcpFileSender);
    let jstr = env
        .new_string(sender_ref.checksum())
        .expect("Failed to create checksum string");
    jstr.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderGetFileSize(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jlong {
    if sender == 0 {
        return 0;
    }
    let sender_ref = &*(sender as *const TcpFileSender);
    sender_ref.file_size() as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderGetProgress(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> f32 {
    if sender == 0 {
        return 0.0;
    }
    let sender_ref = &*(sender as *const TcpFileSender);
    sender_ref.progress()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderCancel(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    if sender == 0 {
        return;
    }
    let sender_ref = &*(sender as *const TcpFileSender);
    sender_ref.cancel();
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderDestroy(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    if sender != 0 {
        let _ = Box::from_raw(sender as *mut TcpFileSender);
    }
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderStart(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
    ip_address: JString,
    port: jint,
    sender_name: JString,
) -> jint {
    if sender == 0 {
        return -1;
    }

    let ip_str = get_string(&mut env, ip_address);
    let name_str = get_string(&mut env, sender_name);

    let ip: std::net::IpAddr = match ip_str.to_str().unwrap_or("").parse() {
        Ok(ip) => ip,
        Err(_) => return -2, // Invalid IP
    };

    let peer_addr = std::net::SocketAddr::new(ip, port as u16);

    let sender_ref = &*(sender as *const TcpFileSender);

    // Blocking call! Should be called from background thread
    match sender_ref.send_to(peer_addr, name_str.to_str().unwrap_or("Android Device")) {
        crate::sender::TransferResult::Success => 0,
        crate::sender::TransferResult::Rejected => 1,
        crate::sender::TransferResult::ChecksumMismatch => 2,
        crate::sender::TransferResult::ConnectionFailed(_) => 3,
        crate::sender::TransferResult::Timeout => 4,
        crate::sender::TransferResult::Cancelled => 5,
        crate::sender::TransferResult::IoError(_) => 6,
    }
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTcpSenderTestLink(
    mut env: JNIEnv,
    _class: JClass,
    ip_address: JString,
    port: jint,
) -> jint {
    let ip_str = get_string(&mut env, ip_address);
    ffi::voidwarp_tcp_sender_test_link(ip_str.as_ptr(), port as u16)
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTransportStartServer(
    _env: JNIEnv,
    _class: JClass,
    port: jint,
) -> jboolean {
    if ffi::voidwarp_transport_start_server(port as u16) {
        1
    } else {
        0
    }
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpTransportPing(
    mut env: JNIEnv,
    _class: JClass,
    ip_address: JString,
    port: jint,
) -> jboolean {
    let ip_str = get_string(&mut env, ip_address);
    if ffi::voidwarp_transport_ping(ip_str.as_ptr(), port as u16) {
        1
    } else {
        0
    }
}
